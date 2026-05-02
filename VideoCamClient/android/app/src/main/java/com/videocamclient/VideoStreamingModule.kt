package com.videocamclient

import android.content.Context
import android.hardware.Camera
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.net.Uri
import android.os.Build
import android.util.Log
import com.facebook.react.bridge.*
import kotlinx.coroutines.*
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Socket
import kotlin.math.min

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
        scope.launch {
            try {
                val socket = DatagramSocket(port)
                socket.broadcast = true
                socket.soTimeout = 5000 // 5秒超时

                val buffer = ByteArray(1024)
                val packet = DatagramPacket(buffer, buffer.size)

                socket.receive(packet)
                val response = String(packet.data, 0, packet.length)

                // 解析服务器响应: VCAM|TCP_PORT|UDP_PORT 或 IP,TCP_PORT,UDP_PORT
                val parts = response.split("|", ",")
                if (parts.size >= 3) {
                    // 支持两种格式: VCAM|9000|9001 或 192.168.1.100,9000,9001
                    val ip = if (parts[0] == "VCAM") {
                        // 如果第一部分是VCAM，IP在发送UDP包的源地址中获取
                        packet.address.hostAddress ?: parts[1]
                    } else {
                        parts[0]
                    }
                    val tcp = parts[1].toInt()
                    val udp = parts[2].toInt()

                    val result = WritableNativeMap().apply {
                        putBoolean("success", true)
                        putString("ip", ip)
                        putInt("tcp", tcp)
                        putInt("udp", udp)
                    }
                    promise.resolve(result)
                } else {
                    promise.reject("DISCOVERY_ERROR", "Invalid server response format")
                }
                socket.close()
            } catch (e: Exception) {
                Log.e(TAG, "Discovery error", e)
                promise.reject("DISCOVERY_ERROR", e.message)
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
                tcpSocket = Socket(ip, tcpPort)
                Log.d(TAG, "TCP connected to $ip:$tcpPort")

                // 初始化UDP Socket
                udpSocket = DatagramSocket()
                Log.d(TAG, "UDP socket created for $ip:$udpPort")

                // 初始化摄像头
                initCamera()

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
            params?.setPreviewSize(VIDEO_WIDTH, VIDEO_HEIGHT)
            params?.setPreviewFpsRange(H264_FRAME_RATE * 1000, H264_FRAME_RATE * 1000)
            camera?.parameters = params

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
            val inputBuffers = mediaCodec?.inputBuffers ?: return
            val outputBuffers = mediaCodec?.outputBuffers ?: return

            val inputBufferIndex = mediaCodec?.dequeueInputBuffer(10000) ?: return
            if (inputBufferIndex >= 0) {
                val inputBuffer = inputBuffers[inputBufferIndex]
                inputBuffer.clear()
                inputBuffer.put(frameData)
                mediaCodec?.queueInputBuffer(inputBufferIndex, 0, frameData.size, System.nanoTime(), 0)
            }

            val bufferInfo = MediaCodec.BufferInfo()
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
        } catch (e: Exception) {
            Log.e(TAG, "Encode frame error", e)
        }
    }

    /**
     * 通过UDP发送数据包
     */
    private fun sendUDPPacket(data: ByteArray) {
        try {
            if (udpSocket?.isConnected == false) return

            val address = InetAddress.getByName(udpTargetIP)
            val packet = DatagramPacket(data, data.size, address, udpTargetPort)
            udpSocket?.send(packet)
        } catch (e: Exception) {
            Log.e(TAG, "UDP send error", e)
        }
    }
}
