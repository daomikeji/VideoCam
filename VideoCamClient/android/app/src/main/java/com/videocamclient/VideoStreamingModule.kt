package com.videocamclient

import android.content.Context
import android.hardware.Camera
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.net.Uri
import android.os.Build
import android.util.Log
import android.graphics.SurfaceTexture
import com.facebook.react.bridge.*
import kotlinx.coroutines.*
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Socket
import kotlin.math.min
import java.net.InetSocketAddress
import java.net.NetworkInterface
import android.net.wifi.WifiManager
class VideoStreamingModule(context: ReactApplicationContext) : ReactContextBaseJavaModule(context) {

    companion object {
        private const val TAG = "VideoStreaming"
        private const val H264_FRAME_RATE = 30
        private const val H264_BIT_RATE = 5000000 // 5 Mbps
        private const val VIDEO_WIDTH = 1280
        private const val VIDEO_HEIGHT = 720
    }

    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private var camera: Camera? = null
    private var previewTexture: SurfaceTexture? = null
    private var mediaCodec: MediaCodec? = null
    private var udpSocket: DatagramSocket? = null
    private var tcpSocket: Socket? = null
    private var isStreaming = false
    private var isFrontCamera = false
    private var isFlashOn = false

    private var udpTargetIP: String = ""
    private var udpTargetPort: Int = 9001
    private var tcpTargetIP: String = ""
    private var tcpTargetPort: Int = 9000

    override fun getName(): String = "VideoStreamingModule"

    /**
     * 发现服务器 - UDP广播监听
     */
    @ReactMethod
    fun startDiscovery(port: Int, promise: Promise) {
        Log.d(TAG, "startDiscovery called, port=$port, thread=${Thread.currentThread().name}")
        scope.launch {
                    var socket: DatagramSocket? = null
        var lock: WifiManager.MulticastLock? = null
        try {
           // 1) 获取 MulticastLock（真机上接收广播更稳定）
            val wifi = reactApplicationContext.applicationContext
                .getSystemService(Context.WIFI_SERVICE) as WifiManager
            lock = wifi.createMulticastLock("videocam-discovery").apply {
                setReferenceCounted(false)
                acquire()
            }

            // 2) 绑定 0.0.0.0:port
            socket = DatagramSocket(null).apply {
                reuseAddress = true
                broadcast = true
                soTimeout =5000
                bind(InetSocketAddress("0.0.0.0", port))
            }
Log.d(TAG, "socket bound on 0.0.0.0:$port, waiting packet...")
            // 3) 可选：主动发探测包（如果你的服务端是请求-响应模式）
//            val probe = "VCAM_DISCOVER".toByteArray()
//            val bcast = DatagramPacket(
//                probe, probe.size,
//                InetAddress.getByName("255.255.255.255"), port
//            )
//            socket.send(bcast)

            // 4) 接收响应
            val buf = ByteArray(1024)
            val packet = DatagramPacket(buf, buf.size)
            socket.receive(packet)
Log.d(TAG, "packet received len=${packet.length} from=${packet.address.hostAddress}:${packet.port}")

            val response = String(packet.data, 0, packet.length).trim()
             Log.e(TAG, "from=${packet.address.hostAddress}:${packet.port}, response=$response")
            val parts = response.split("|", ",").map { it.trim() }

            if (parts.size >= 3) {
                val ip = if (parts[0] == "VCAM") packet.address.hostAddress ?: "" else parts[0]
                val tcp = parts[1].toInt()
                val udp = parts[2].toInt()

                val result = WritableNativeMap().apply {
                    putBoolean("success", true)
                    putString("ip", ip)
                    putInt("tcp", tcp)
                    putInt("udp", udp)
                    putString("raw", response)
                }
                promise.resolve(result)
            } else {
                promise.reject("DISCOVERY_ERROR", "Invalid server response: $response")
            }
        } catch (e: Exception) {
            Log.e(TAG, "Discovery error", e)
            promise.reject("DISCOVERY_ERROR", e.message)
        } finally {
            try { socket?.close() } catch (_: Exception) {}
            try { if (lock?.isHeld == true) lock?.release() } catch (_: Exception) {}
        }
        }
    }

    /**
     * 连接并开始流传输
     */
    @ReactMethod
    fun connectAndStream(ip: String, tcpPort: Int, udpPort: Int, promise: Promise) {
        scope.launch {
            try {
                udpTargetIP = ip
                udpTargetPort = udpPort
                tcpTargetIP = ip
                tcpTargetPort = tcpPort

                // 初始化TCP连接
                tcpSocket = Socket()
                tcpSocket?.connect(InetSocketAddress(ip, tcpPort), 5000)
                Log.d(TAG, "TCP connected to $ip:$tcpPort")

                // 初始化UDP Socket 并连接目标地址
                udpSocket = DatagramSocket()
                try {
                    udpSocket?.connect(InetAddress.getByName(ip), udpPort)
                    Log.d(TAG, "UDP socket connected to $ip:$udpPort")
                } catch (e: Exception) {
                    Log.w(TAG, "UDP connect failed, will send unconnected: ${e.message}")
                }

                // 初始化摄像头（在主线程）
                try {
                    reactApplicationContext.runOnUiQueueThread { initCamera() }
                } catch (e: Exception) {
                    // fallback to direct call if runOnUiQueueThread unavailable
                    initCamera()
                }

                // 启动编码器
                startEncoder()

                isStreaming = true

                val result = WritableNativeMap().apply {
                    putBoolean("success", true)
                    putString("message", "Connected and streaming")
                }
                promise.resolve(result)
            } catch (e: Exception) {
                Log.e(TAG, "Connection error", e)
                promise.reject("CONNECTION_ERROR", e.message)
            }
        }
    }

    /**
     * 断开连接
     */
    @ReactMethod
    fun disconnect(promise: Promise) {
        scope.launch {
            try {
                isStreaming = false

                // 停止摄像头
                camera?.stopPreview()
                camera?.release()
                camera = null
                try { previewTexture?.release() } catch (_: Exception) {}
                previewTexture = null

                // 停止编码器
                mediaCodec?.stop()
                mediaCodec?.release()
                mediaCodec = null

                // 关闭Socket
                tcpSocket?.close()
                tcpSocket = null

                udpSocket?.close()
                udpSocket = null

                Log.d(TAG, "Disconnected")
                promise.resolve(true)
            } catch (e: Exception) {
                Log.e(TAG, "Disconnect error", e)
                promise.reject("DISCONNECT_ERROR", e.message)
            }
        }
    }

    /**
     * 发送控制命令（TCP）
     */
    @ReactMethod
    fun sendControlCommand(command: String, promise: Promise) {
        scope.launch {
            try {
                if (tcpSocket?.isConnected != true) {
                    promise.reject("TCP_ERROR", "TCP socket not connected")
                    return@launch
                }

                // 处理命令
                when {
                    command.startsWith("SWITCH_CAMERA:") -> {
                        val cameraMode = command.substringAfter("SWITCH_CAMERA:")
                        switchCamera(cameraMode == "FRONT")
                    }
                    command.startsWith("TOGGLE_FLASH:") -> {
                        val flashMode = command.substringAfter("TOGGLE_FLASH:")
                        toggleFlash(flashMode == "ON")
                    }
                }

                // 通过TCP发送命令到服务器
                tcpSocket?.outputStream?.write(command.toByteArray())
                tcpSocket?.outputStream?.flush()

                Log.d(TAG, "Command sent: $command")
                promise.resolve(true)
            } catch (e: Exception) {
                Log.e(TAG, "Command error", e)
                promise.reject("COMMAND_ERROR", e.message)
            }
        }
    }

    /**
     * 初始化摄像头
     */
    private fun initCamera() {
        try {
            val cameraId = if (isFrontCamera) Camera.CameraInfo.CAMERA_FACING_FRONT else Camera.CameraInfo.CAMERA_FACING_BACK
            var targetCameraId = 0

            for (i in 0 until Camera.getNumberOfCameras()) {
                val info = Camera.CameraInfo()
                Camera.getCameraInfo(i, info)
                if (info.facing == cameraId) {
                    targetCameraId = i
                    break
                }
            }

            camera = Camera.open(targetCameraId)

            val params = camera?.parameters
            params?.previewFormat = android.graphics.ImageFormat.NV21

            // choose supported preview size close to desired
            val supported = params?.supportedPreviewSizes
            if (supported != null && supported.isNotEmpty()) {
                var best = supported[0]
                for (s in supported) {
                    if (kotlin.math.abs(s.width - VIDEO_WIDTH) + kotlin.math.abs(s.height - VIDEO_HEIGHT) <
                        kotlin.math.abs(best.width - VIDEO_WIDTH) + kotlin.math.abs(best.height - VIDEO_HEIGHT)
                    ) best = s
                }
                params.setPreviewSize(best.width, best.height)
            } else {
                params?.setPreviewSize(VIDEO_WIDTH, VIDEO_HEIGHT)
            }

            // set fps range if available
            try {
                params?.let { safeParams ->
                    val ranges = safeParams.supportedPreviewFpsRange
                    if (!ranges.isNullOrEmpty()) {
                        var bestRange = ranges[0]
                        for (r in ranges) {
                            if (r[1] >= H264_FRAME_RATE * 1000) { bestRange = r; break }
                        }
                        safeParams.setPreviewFpsRange(bestRange[0], bestRange[1])
                    } else {
                        safeParams.setPreviewFpsRange(H264_FRAME_RATE * 1000, H264_FRAME_RATE * 1000)
                    }
                }
            } catch (_: Exception) {}

            camera?.parameters = params

            // Use a SurfaceTexture so camera actually starts preview on devices without UI
            try {
                previewTexture = SurfaceTexture(10)
                camera?.setPreviewTexture(previewTexture)
            } catch (e: Exception) {
                Log.w(TAG, "setPreviewTexture failed: ${e.message}")
            }

            camera?.setPreviewCallback { data, _ ->
                if (isStreaming && mediaCodec != null) {
                    encodeFrame(data)
                }
            }

            camera?.startPreview()
            Log.d(TAG, "Camera initialized: ${if (isFrontCamera) "FRONT" else "BACK"}")
        } catch (e: Exception) {
            Log.e(TAG, "Camera init error", e)
        }
    }

    /**
     * 切换摄像头
     */
    private fun switchCamera(toFront: Boolean) {
        try {
            camera?.stopPreview()
            camera?.release()
            isFrontCamera = toFront
            initCamera()
            Log.d(TAG, "Camera switched to: ${if (toFront) "FRONT" else "BACK"}")
        } catch (e: Exception) {
            Log.e(TAG, "Switch camera error", e)
        }
    }

    /**
     * 切换闪光灯
     */
    private fun toggleFlash(on: Boolean) {
        try {
            if (isFrontCamera) {
                Log.w(TAG, "Front camera doesn't support flash")
                return
            }

            val params = camera?.parameters
            if (on) {
                params?.flashMode = Camera.Parameters.FLASH_MODE_TORCH
            } else {
                params?.flashMode = Camera.Parameters.FLASH_MODE_OFF
            }
            camera?.parameters = params
            isFlashOn = on
            Log.d(TAG, "Flash toggled: $on")
        } catch (e: Exception) {
            Log.e(TAG, "Toggle flash error", e)
        }
    }

    /**
     * 启动H.264编码器
     */
    private fun startEncoder() {
        try {
            val mediaFormat = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, VIDEO_WIDTH, VIDEO_HEIGHT)
            mediaFormat.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420SemiPlanar)
            mediaFormat.setInteger(MediaFormat.KEY_BIT_RATE, H264_BIT_RATE)
            mediaFormat.setInteger(MediaFormat.KEY_FRAME_RATE, H264_FRAME_RATE)
            mediaFormat.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)

            mediaCodec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            mediaCodec?.configure(mediaFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            mediaCodec?.start()

            Log.d(TAG, "H.264 encoder started")
        } catch (e: Exception) {
            Log.e(TAG, "Encoder start error", e)
        }
    }

    /**
     * 编码帧并通过UDP发送
     */
    private fun encodeFrame(frameData: ByteArray) {
        try {
            val bufferInfo = MediaCodec.BufferInfo()

            if (Build.VERSION.SDK_INT >= 21) {
                val inputBufferIndex = mediaCodec?.dequeueInputBuffer(10000) ?: return
                if (inputBufferIndex >= 0) {
                    val inputBuffer = mediaCodec?.getInputBuffer(inputBufferIndex)
                    inputBuffer?.clear()
                    inputBuffer?.put(frameData)
                    mediaCodec?.queueInputBuffer(inputBufferIndex, 0, frameData.size, System.nanoTime() / 1000, 0)
                }

                var outputBufferIndex = mediaCodec?.dequeueOutputBuffer(bufferInfo, 0) ?: return
                while (outputBufferIndex >= 0) {
                    val outputBuffer = mediaCodec?.getOutputBuffer(outputBufferIndex)
                    val chunk = ByteArray(bufferInfo.size)
                    outputBuffer?.get(chunk)

                    // 通过UDP发送H.264数据包
                    sendUDPPacket(chunk)

                    mediaCodec?.releaseOutputBuffer(outputBufferIndex, false)
                    outputBufferIndex = mediaCodec?.dequeueOutputBuffer(bufferInfo, 0) ?: -1
                }
            } else {
                val inputBuffers = mediaCodec?.inputBuffers ?: return
                val outputBuffers = mediaCodec?.outputBuffers ?: return

                val inputBufferIndex = mediaCodec?.dequeueInputBuffer(10000) ?: return
                if (inputBufferIndex >= 0) {
                    val inputBuffer = inputBuffers[inputBufferIndex]
                    inputBuffer.clear()
                    inputBuffer.put(frameData)
                    mediaCodec?.queueInputBuffer(inputBufferIndex, 0, frameData.size, System.nanoTime() / 1000, 0)
                }

                var outputBufferIndex = mediaCodec?.dequeueOutputBuffer(bufferInfo, 0) ?: return
                while (outputBufferIndex >= 0) {
                    val outputBuffer = outputBuffers[outputBufferIndex]
                    val chunk = ByteArray(bufferInfo.size)
                    outputBuffer.get(chunk)

                    // 通过UDP发送H.264数据包
                    sendUDPPacket(chunk)

                    mediaCodec?.releaseOutputBuffer(outputBufferIndex, false)
                    outputBufferIndex = mediaCodec?.dequeueOutputBuffer(bufferInfo, 0) ?: -1
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Encode frame error", e)
        }
    }

    /**
     * 通过UDP发送数据包
     */
    private fun sendUDPPacket(data: ByteArray) {
        try {
            val address = InetAddress.getByName(udpTargetIP)
            val packet = DatagramPacket(data, data.size, address, udpTargetPort)
            udpSocket?.send(packet)
        } catch (e: Exception) {
            Log.e(TAG, "UDP send error", e)
        }
    }
}
