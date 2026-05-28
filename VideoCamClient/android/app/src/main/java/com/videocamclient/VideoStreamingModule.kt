package com.videocamclient

import android.content.Context
import android.graphics.SurfaceTexture
import android.hardware.Camera
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.net.Uri
import android.os.Build
import android.util.Log
import android.view.TextureView
import android.view.ViewGroup
import android.view.View
import android.opengl.GLES20
import android.opengl.GLES11Ext
import android.os.Handler
import android.os.HandlerThread
import android.view.Surface
import com.facebook.react.bridge.*
import com.facebook.react.uimanager.NativeViewHierarchyManager
import com.facebook.react.uimanager.UIManagerModule
import kotlinx.coroutines.*
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Socket
import java.net.InetSocketAddress
import java.net.NetworkInterface
import android.net.wifi.WifiManager
import kotlin.coroutines.resume
import kotlinx.coroutines.suspendCancellableCoroutine
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
    private var previewTexture: SurfaceTexture? = null
    private var ownsPreviewTexture = false
    private var mediaCodec: MediaCodec? = null
    // EGL / GL rendering for encoder input surface
    private var eglCore: EglCore? = null
    private var windowSurface: WindowSurface? = null
    private var glThread: HandlerThread? = null
    private var glHandler: Handler? = null
    private var encoderTextureId: Int = -1
    private var encoderInputSurface: Surface? = null
    // stored codec config (SPS/PPS) in Annex-B form
    private var spsPps: ByteArray? = null
    // UDP packet settings
    private var UDP_MTU = 1200 // total packet size limit (bytes)
    private var udpPacketVersion = 1
    private val UDP_FRAGMENT_HEADER_SIZE = 12
    private val UDP_FRAGMENT_MAGIC = byteArrayOf('V'.toByte(), 'C'.toByte(), 'A'.toByte(), 'M'.toByte())
    private var udpSocket: DatagramSocket? = null
    private var tcpSocket: Socket? = null
    private var isStreaming = false
    private var udpFrameId = 0
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
                sendStreamDescription()

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

    @ReactMethod
    fun connectAndStreamWithPreview(ip: String, tcpPort: Int, udpPort: Int, previewViewId: Int, promise: Promise) {
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
                sendStreamDescription()

                // 初始化UDP Socket 并连接目标地址
                udpSocket = DatagramSocket()
                try {
                    udpSocket?.connect(InetAddress.getByName(ip), udpPort)
                    Log.d(TAG, "UDP socket connected to $ip:$udpPort")
                } catch (e: Exception) {
                    Log.w(TAG, "UDP connect failed, will send unconnected: ${e.message}")
                }

                // 获取预览 SurfaceTexture
                val surfaceTexture = getPreviewSurfaceTexture(previewViewId)
                Log.d(TAG, "resolved preview surfaceTexture=$surfaceTexture for viewId=$previewViewId")
                if (surfaceTexture == null) {
                    Log.e(TAG, "Preview surface not available for viewId=$previewViewId")
                    promise.reject("PREVIEW_ERROR", "Preview surface not available")
                    return@launch
                }

                // 初始化摄像头（在主线程）
                withContext(Dispatchers.Main) {
                    initCamera(surfaceTexture)
                }

                // 直接使用 buffer 输入编码，保留 TextureView 预览
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

    private suspend fun getPreviewSurfaceTexture(viewId: Int): SurfaceTexture? = suspendCancellableCoroutine { cont ->
        val currentTexView = CameraPreviewManager.currentTextureView
        if (currentTexView != null) {
            Log.d(TAG, "got current TextureView from manager, isAvailable=${currentTexView.isAvailable}")
            if (currentTexView.isAvailable) {
                cont.resume(currentTexView.surfaceTexture)
                return@suspendCancellableCoroutine
            }
            currentTexView.surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                override fun onSurfaceTextureAvailable(surface: SurfaceTexture, width: Int, height: Int) {
                    Log.d(TAG, "manager TextureView surface available, width=$width height=$height")
                    currentTexView.surfaceTextureListener = null
                    cont.resume(surface)
                }

                override fun onSurfaceTextureSizeChanged(surface: SurfaceTexture, width: Int, height: Int) {}

                override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean = false

                override fun onSurfaceTextureUpdated(surface: SurfaceTexture) {}
            }
            return@suspendCancellableCoroutine
        }

        val uiManager = reactApplicationContext.getNativeModule(UIManagerModule::class.java)
        if (uiManager == null) {
            cont.resume(null)
            return@suspendCancellableCoroutine
        }

        uiManager.addUIBlock { nativeViewHierarchyManager: NativeViewHierarchyManager ->
            try {
                val view = nativeViewHierarchyManager.resolveView(viewId)
                Log.d(TAG, "resolved view for preview id=$viewId class=${view?.javaClass?.name} view=${view}")

                fun findTextureView(search: View?): TextureView? {
                    if (search == null) return null
                    if (search is TextureView) return search
                    if (search is ViewGroup) {
                        for (i in 0 until search.childCount) {
                            val found = findTextureView(search.getChildAt(i))
                            if (found != null) return found
                        }
                    }
                    return null
                }

                val texView = findTextureView(view)
                if (texView != null) {
                    Log.d(TAG, "preview TextureView found, isAvailable=${texView.isAvailable}")
                    if (texView.isAvailable) {
                        cont.resume(texView.surfaceTexture)
                    } else {
                        texView.surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                            override fun onSurfaceTextureAvailable(surface: SurfaceTexture, width: Int, height: Int) {
                                Log.d(TAG, "preview TextureView surface available, width=$width height=$height")
                                texView.surfaceTextureListener = null
                                cont.resume(surface)
                            }

                            override fun onSurfaceTextureSizeChanged(surface: SurfaceTexture, width: Int, height: Int) {}

                            override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean = false

                            override fun onSurfaceTextureUpdated(surface: SurfaceTexture) {}
                        }
                    }
                } else {
                    Log.e(TAG, "Preview TextureView not found for viewId=$viewId, resolvedView=${view?.javaClass?.name}")
                    cont.resume(null)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Preview retrieval error", e)
                cont.resume(null)
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
                if (ownsPreviewTexture) {
                    try { previewTexture?.release() } catch (_: Exception) {}
                }
                previewTexture = null
                ownsPreviewTexture = false

                // release GL / encoder surface
                try { glHandler?.post { windowSurface?.release() } } catch (_: Exception) {}
                try { glThread?.quitSafely() } catch (_: Exception) {}
                glHandler = null
                glThread = null
                windowSurface = null
                eglCore?.release()
                eglCore = null
                encoderInputSurface = null

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
    private fun initCamera(previewSurfaceTexture: SurfaceTexture? = null) {
        try {
            if (previewTexture != null && ownsPreviewTexture) {
                try { previewTexture?.release() } catch (_: Exception) {}
                previewTexture = null
                ownsPreviewTexture = false
            }

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

            try {
                val textureToUse = previewSurfaceTexture ?: previewTexture
                if (textureToUse != null) {
                    previewTexture = textureToUse
                    ownsPreviewTexture = false
                } else {
                    previewTexture = SurfaceTexture(10)
                    ownsPreviewTexture = true
                }

                previewTexture?.let {
                    try {
                        val previewSize = camera?.parameters?.previewSize
                        if (previewSize != null) {
                            it.setDefaultBufferSize(previewSize.width, previewSize.height)
                        }
                    } catch (ignored: Exception) {
                        Log.w(TAG, "setDefaultBufferSize failed: ${ignored.message}")
                    }
                }

                Log.d(TAG, "using previewTexture=$previewTexture, ownsPreviewTexture=$ownsPreviewTexture")
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
            if (previewTexture != null && !ownsPreviewTexture) {
                initCamera(previewTexture)
            } else {
                initCamera()
            }
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

            // start output drain loop
            drainEncoder()
            Log.d(TAG, "H.264 encoder started")
        } catch (e: Exception) {
            Log.e(TAG, "Encoder start error", e)
        }
    }

    /**
     * 启动MediaCodec并使用InputSurface + EGL渲染路径
     */
    private fun startEncoderWithSurface() {
        try {
            val mediaFormat = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, VIDEO_WIDTH, VIDEO_HEIGHT)
            mediaFormat.setInteger(MediaFormat.KEY_BIT_RATE, H264_BIT_RATE)
            mediaFormat.setInteger(MediaFormat.KEY_FRAME_RATE, H264_FRAME_RATE)
            mediaFormat.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)

            mediaCodec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            mediaCodec?.configure(mediaFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)

            // create input surface
            encoderInputSurface = mediaCodec?.createInputSurface()
            mediaCodec?.start()

            // setup EGL and GL thread to render camera preview into encoder surface
            eglCore = EglCore(null, EglCore.FLAG_RECORDABLE)
            encoderInputSurface?.let { s ->
                windowSurface = WindowSurface(eglCore!!, s, true)
            }

            glThread = HandlerThread("EncoderGLThread")
            glThread?.start()
            glHandler = Handler(glThread!!.looper)

            glHandler?.post {
                try {
                    windowSurface?.makeCurrent()
                    TextureRenderer.init()

                    // create external texture to draw from camera SurfaceTexture
                    val tex = IntArray(1)
                    GLES20.glGenTextures(1, tex, 0)
                    encoderTextureId = tex[0]
                    GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, encoderTextureId)
                    GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_NEAREST)
                    GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
                    GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
                    GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)

                    // attach preview SurfaceTexture to this OpenGL texture if available
                    try {
                        previewTexture?.attachToGLContext(encoderTextureId)
                    } catch (e: Exception) {
                        Log.w(TAG, "attachToGLContext failed: ${e.message}")
                    }

                    // frame callback -> render into encoder surface
                    previewTexture?.setOnFrameAvailableListener({ surfaceTexture ->
                        glHandler?.post {
                            try {
                                surfaceTexture.updateTexImage()
                                TextureRenderer.draw(encoderTextureId)
                                windowSurface?.swapBuffers()
                            } catch (e: Exception) {
                                Log.e(TAG, "GL render error", e)
                            }
                        }
                    }, Handler(glThread!!.looper))
                } catch (e: Exception) {
                    Log.e(TAG, "GL thread init error", e)
                }
            }

            // start output drain loop (encoder produces output even without buffer input)
            drainEncoder()

            Log.d(TAG, "H.264 encoder (surface) started")
        } catch (e: Exception) {
            Log.e(TAG, "Encoder start error", e)
        }
    }

    /**
     * Drain encoder output in a coroutine and handle SPS/PPS, NAL framing and UDP fragmentation
     */
    private fun drainEncoder() {
        scope.launch {
            try {
                val bufferInfo = MediaCodec.BufferInfo()
                while (mediaCodec != null && isStreaming) {
                    val outputIndex = mediaCodec?.dequeueOutputBuffer(bufferInfo, 10000) ?: -1
                    if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                        // no output available
                    } else if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                        val format = mediaCodec?.outputFormat
                        // extract csd-0/csd-1 if present
                        try {
                            val csd0 = format?.getByteBuffer("csd-0")
                            val csd1 = format?.getByteBuffer("csd-1")
                            if (csd0 != null && csd1 != null) {
                                val b0 = ByteArray(csd0.remaining())
                                csd0.get(b0)
                                val b1 = ByteArray(csd1.remaining())
                                csd1.get(b1)
                                // combine into Annex-B: prepend start codes
                                val combined = ByteArray(4 + b0.size + 4 + b1.size)
                                var pos = 0
                                System.arraycopy(byteArrayOf(0x00.toByte(),0x00.toByte(),0x00.toByte(),0x01.toByte()), 0, combined, pos, 4); pos += 4
                                System.arraycopy(b0, 0, combined, pos, b0.size); pos += b0.size
                                System.arraycopy(byteArrayOf(0x00.toByte(),0x00.toByte(),0x00.toByte(),0x01.toByte()), 0, combined, pos, 4); pos += 4
                                System.arraycopy(b1, 0, combined, pos, b1.size)
                                spsPps = combined
                                Log.d(TAG, "Extracted SPS/PPS, size=${combined.size}")
                            }
                        } catch (e: Exception) {
                            Log.w(TAG, "Failed to extract csd", e)
                        }
                    } else if (outputIndex >= 0) {
                        val outBytes = ByteArray(bufferInfo.size)
                        try {
                            if (Build.VERSION.SDK_INT >= 21) {
                                val outputBuffer = mediaCodec?.getOutputBuffer(outputIndex)
                                outputBuffer?.get(outBytes)
                            } else {
                                val outputBuffers = mediaCodec?.outputBuffers
                                if (outputBuffers != null && outputIndex >= 0 && outputIndex < outputBuffers.size) {
                                    val bb = outputBuffers[outputIndex]
                                    bb.position(0)
                                    bb.get(outBytes)
                                }
                            }
                        } catch (e: Exception) {
                            Log.w(TAG, "Read output buffer failed", e)
                        }

                        // handle codec config (SPS/PPS) flags
                        if ((bufferInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
                            // already handled via format change; ignore
                        } else {
                            if ((bufferInfo.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0 && spsPps != null) {
                                val annexbPayload = convertLengthPrefixedToAnnexB(outBytes)
                                val combined = ByteArray(spsPps!!.size + annexbPayload.size)
                                System.arraycopy(spsPps!!, 0, combined, 0, spsPps!!.size)
                                System.arraycopy(annexbPayload, 0, combined, spsPps!!.size, annexbPayload.size)
                                sendH264NalOverUdp(combined)
                            } else {
                                val annexb = convertLengthPrefixedToAnnexB(outBytes)
                                sendH264NalOverUdp(annexb)
                            }
                        }

                        mediaCodec?.releaseOutputBuffer(outputIndex, false)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "drainEncoder error", e)
            }
        }
    }

    /** Convert Android length-prefixed NAL stream to Annex-B (0x00000001) */
    private fun convertLengthPrefixedToAnnexB(input: ByteArray): ByteArray {
        try {
            var pos = 0
            val out = java.io.ByteArrayOutputStream()
            while (pos + 4 <= input.size) {
                val nalLen = ((input[pos].toInt() and 0xFF) shl 24) or
                        ((input[pos+1].toInt() and 0xFF) shl 16) or
                        ((input[pos+2].toInt() and 0xFF) shl 8) or
                        (input[pos+3].toInt() and 0xFF)
                pos += 4
                if (pos + nalLen > input.size) break
                // write start code
                out.write(byteArrayOf(0x00.toByte(),0x00.toByte(),0x00.toByte(),0x01.toByte()))
                out.write(input, pos, nalLen)
                pos += nalLen
            }
            return out.toByteArray()
        } catch (e: Exception) {
            Log.e(TAG, "convertLengthPrefixedToAnnexB error", e)
            return input
        }
    }

    /**
     * Send a raw H.264 Annex-B packet over UDP.
     * The server must receive complete H.264 payloads and decode them directly.
     */
    private fun sendH264NalOverUdp(data: ByteArray) {
        try {
            if (udpSocket == null) return

            val nals = splitAnnexBIntoNals(data)
            if (nals.isEmpty()) {
                Log.w(TAG, "No H.264 NAL units found in Annex-B stream")
                return
            }

            val frameId = nextUdpFrameId()
            for ((nalIndex, nal) in nals.withIndex()) {
                if (nal.size <= UDP_MTU) {
                    if (!sendUDPPacket(nal)) {
                        Log.e(TAG, "UDP send failed for NAL size ${nal.size}")
                    }
                } else {
                    sendNalFragments(nal, frameId, nalIndex)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "sendH264NalOverUdp error", e)
        }
    }

    private fun sendNalFragments(nal: ByteArray, frameId: Int, nalIndex: Int) {
        try {
            val payloadMax = UDP_MTU - UDP_FRAGMENT_HEADER_SIZE
            if (payloadMax <= 0) {
                Log.e(TAG, "Invalid UDP_MTU=$UDP_MTU, cannot fragment NAL")
                return
            }

            var offset = 0
            var fragmentIndex = 0
            while (offset < nal.size) {
                val chunkSize = min(payloadMax, nal.size - offset)
                val flags = ((if (offset == 0) 0x1 else 0) or (if (offset + chunkSize >= nal.size) 0x2 else 0)).toByte()
                val header = createUdpFragmentHeader(flags, frameId, nalIndex, fragmentIndex)
                val packetData = ByteArray(header.size + chunkSize)
                System.arraycopy(header, 0, packetData, 0, header.size)
                System.arraycopy(nal, offset, packetData, header.size, chunkSize)

                if (!sendUDPPacket(packetData)) {
                    Log.e(TAG, "UDP fragment send failed for nalIndex=$nalIndex fragment=$fragmentIndex size=${packetData.size}")
                } else {
                    Log.d(TAG, "UDP fragment sent nalIndex=$nalIndex fragment=$fragmentIndex size=${packetData.size}")
                }

                offset += chunkSize
                fragmentIndex += 1
            }
        } catch (e: Exception) {
            Log.e(TAG, "sendNalFragments error", e)
        }
    }

    private fun createUdpFragmentHeader(flags: Byte, frameId: Int, nalIndex: Int, fragmentIndex: Int): ByteArray {
        val header = ByteArray(UDP_FRAGMENT_HEADER_SIZE)
        System.arraycopy(UDP_FRAGMENT_MAGIC, 0, header, 0, UDP_FRAGMENT_MAGIC.size)
        header[4] = udpPacketVersion.toByte()
        header[5] = flags
        header[6] = ((frameId shr 8) and 0xFF).toByte()
        header[7] = (frameId and 0xFF).toByte()
        header[8] = ((nalIndex shr 8) and 0xFF).toByte()
        header[9] = (nalIndex and 0xFF).toByte()
        header[10] = ((fragmentIndex shr 8) and 0xFF).toByte()
        header[11] = (fragmentIndex and 0xFF).toByte()
        return header
    }

    private fun nextUdpFrameId(): Int {
        udpFrameId = (udpFrameId + 1) and 0xFFFF
        return udpFrameId
    }

    private fun splitAnnexBIntoNals(data: ByteArray): List<ByteArray> {
        val result = mutableListOf<ByteArray>()
        var pos = 0

        while (pos + 3 < data.size) {
            val startCodeLen = when {
                pos + 4 <= data.size && data[pos] == 0.toByte() && data[pos + 1] == 0.toByte() && data[pos + 2] == 0.toByte() && data[pos + 3] == 1.toByte() -> 4
                data[pos] == 0.toByte() && data[pos + 1] == 0.toByte() && data[pos + 2] == 1.toByte() -> 3
                else -> {
                    pos += 1
                    continue
                }
            }

            val nalStart = pos
            pos += startCodeLen
            var next = pos
            while (next + 3 < data.size) {
                if (data[next] == 0.toByte() && data[next + 1] == 0.toByte() &&
                    (data[next + 2] == 1.toByte() || (next + 4 <= data.size && data[next + 2] == 0.toByte() && data[next + 3] == 1.toByte()))
                ) {
                    break
                }
                next += 1
            }

            val nalEnd = if (next < data.size) next else data.size
            result.add(data.copyOfRange(nalStart, nalEnd))
            pos = next
        }

        return result
    }

    private fun sendStreamDescription() {
        try {
            if (tcpSocket?.isConnected != true) return
            val desc = "VCAM|VIDEO|H264|annexb|w=$VIDEO_WIDTH|h=$VIDEO_HEIGHT|fps=$H264_FRAME_RATE|mtu=$UDP_MTU|ver=$udpPacketVersion\n"
            tcpSocket?.getOutputStream()?.write(desc.toByteArray())
            tcpSocket?.getOutputStream()?.flush()
            Log.d(TAG, "Sent stream description: $desc")
        } catch (e: Exception) {
            Log.w(TAG, "sendStreamDescription failed", e)
        }
    }

    private fun sendFrameOverTcp(data: ByteArray): Boolean {
        try {
            if (tcpSocket?.isConnected != true) return false
            val lengthPrefix = ByteArray(4)
            lengthPrefix[0] = ((data.size shr 24) and 0xFF).toByte()
            lengthPrefix[1] = ((data.size shr 16) and 0xFF).toByte()
            lengthPrefix[2] = ((data.size shr 8) and 0xFF).toByte()
            lengthPrefix[3] = (data.size and 0xFF).toByte()
            tcpSocket?.getOutputStream()?.write(lengthPrefix)
            tcpSocket?.getOutputStream()?.write(data)
            tcpSocket?.getOutputStream()?.flush()
            Log.d(TAG, "Sent frame over TCP, size=${data.size}")
            return true
        } catch (e: Exception) {
            Log.e(TAG, "sendFrameOverTcp failed", e)
            return false
        }
    }

    /**
     * 编码帧并通过UDP发送
     */
    private fun encodeFrame(frameData: ByteArray) {
        try {
            // if using surface-input pipeline, frames are rendered via GL, skip buffer-input path
            if (encoderInputSurface != null) return

            val nv12Frame = convertNV21ToNV12(frameData)
            val inputBufferIndex = mediaCodec?.dequeueInputBuffer(10000) ?: return
            if (inputBufferIndex >= 0) {
                val inputBuffer = mediaCodec?.getInputBuffer(inputBufferIndex)
                inputBuffer?.clear()
                inputBuffer?.put(nv12Frame)
                mediaCodec?.queueInputBuffer(inputBufferIndex, 0, nv12Frame.size, System.nanoTime() / 1000, 0)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Encode frame error", e)
        }
    }

    private fun convertNV21ToNV12(nv21: ByteArray): ByteArray {
        val ySize = nv21.size * 2 / 3
        val frameSize = ySize + ySize / 2
        if (nv21.size < frameSize) {
            return nv21
        }
        val nv12 = ByteArray(frameSize)
        System.arraycopy(nv21, 0, nv12, 0, ySize)
        var i = 0
        while (i < ySize / 2) {
            nv12[ySize + i] = nv21[ySize + i + 1]
            nv12[ySize + i + 1] = nv21[ySize + i]
            i += 2
        }
        return nv12
    }

    /**
     * 通过UDP发送数据包
     */
    /**
     * Send single UDP packet, return true if success.
     * On IOException with EMSGSIZE, caller may retry with smaller MTU.
     */
    private fun sendUDPPacket(data: ByteArray): Boolean {
        try {
            val address = InetAddress.getByName(udpTargetIP)
            val packet = DatagramPacket(data, data.size, address, udpTargetPort)
            udpSocket?.send(packet)
            Log.d(TAG, "UDP packet sent, size=${data.size}, target=$udpTargetIP:$udpTargetPort")
            return true
        } catch (e: IOException) {
            val msg = e.message ?: ""
            Log.e(TAG, "UDP send IO error: $msg")
            return false
        } catch (e: Exception) {
            Log.e(TAG, "UDP send error", e)
            return false
        }
    }
}
