# TCP + UDP 视频传输协议文档

## ?? 协议架构

你当前使用的 **TCP + UDP 双通道架构**是一个非常合理的选择：

### 为什么 TCP + UDP 比 RTSP/WebRTC 更适合？

| 协议 | 优点 | 缺点 | 适用场景 |
|------|------|------|---------|
| **TCP + UDP** | ? 实现简单<br>? 低延迟<br>? 可控性强<br>? 不需要额外库 | ?? 需要自己处理丢包<br>?? 需要自定义协议 | 局域网、点对点传输 |
| RTSP | ? 标准协议<br>? 成熟 | ? 复杂<br>? 延迟较高<br>? 需要额外库 | 监控、广播 |
| WebRTC | ? P2P<br>? NAT穿透 | ? 非常复杂<br>? 需要信令服务器<br>? 资源占用大 | 视频会议、浏览器 |

### 你的架构优势

? **TCP (端口 9000)** - 控制通道
- 可靠传输
- 用于发送命令（切换摄像头、闪光灯、分辨率等）
- 接收服务器状态

? **UDP (端口 9001)** - 视频数据通道
- 低延迟
- 适合实时视频流
- 容忍少量丢包

? **UDP 广播 (端口 9005)** - 服务发现
- 手机自动发现服务器
- 无需手动输入 IP

---

## ?? 协议规范

### 1. 服务发现协议

#### 服务器端广播
```
格式: VCAM|TCP_PORT|UDP_PORT
示例: VCAM|9000|9001
频率: 每 2 秒广播一次
端口: UDP 9005
```

#### 手机端接收
```kotlin
// Android 示例
val socket = DatagramSocket(9005)
socket.broadcast = true

while (true) {
    val buffer = ByteArray(1024)
    val packet = DatagramPacket(buffer, buffer.size)
    socket.receive(packet)
    
    val message = String(packet.data, 0, packet.length)
    if (message.startsWith("VCAM|")) {
        val parts = message.split("|")
        val serverIP = packet.address.hostAddress
        val tcpPort = parts[1].toInt()
        val udpPort = parts[2].toInt()
        
        // 连接到服务器
        connectToServer(serverIP, tcpPort, udpPort)
    }
}
```

```swift
// iOS 示例
import Network

let connection = NWConnection(
    host: .ipv4(.broadcast),
    port: 9005,
    using: .udp
)

connection.receiveMessage { data, context, isComplete, error in
    if let data = data,
       let message = String(data: data, encoding: .utf8),
       message.hasPrefix("VCAM|") {
        let parts = message.split(separator: "|")
        let tcpPort = Int(parts[1])!
        let udpPort = Int(parts[2])!
        // 连接到服务器
    }
}
```

---

### 2. TCP 控制协议

#### 连接握手
```
1. 手机连接到服务器 TCP 端口 9000
2. 服务器接受连接
3. 手机发送 HELLO 消息
4. 服务器回复 ACK
```

#### 命令格式

所有命令使用 UTF-8 编码的文本，以换行符结尾：

##### A. 切换摄像头
```
命令: SWITCH_CAMERA:FRONT
命令: SWITCH_CAMERA:BACK
响应: ACK
```

##### B. 闪光灯控制
```
命令: TOGGLE_FLASH:ON
命令: TOGGLE_FLASH:OFF
响应: ACK
```

##### C. 设置分辨率
```
命令: SET_RESOLUTION:1920:1080
命令: SET_RESOLUTION:1280:720
命令: SET_RESOLUTION:640:480
响应: ACK
```

##### D. 设置帧率
```
命令: SET_FPS:30
命令: SET_FPS:15
响应: ACK
```

##### E. 设置比特率
```
命令: SET_BITRATE:2000000  (2Mbps)
响应: ACK
```

##### F. 开始/停止视频流
```
命令: START_STREAM
命令: STOP_STREAM
响应: ACK
```

##### G. 心跳包（可选）
```
命令: PING
响应: PONG
频率: 每 5 秒
```

#### Android 实现示例
```kotlin
class TCPControlClient(private val serverIP: String, private val port: Int) {
    private var socket: Socket? = null
    private var outputStream: OutputStream? = null
    private var inputStream: InputStream? = null
    
    suspend fun connect() = withContext(Dispatchers.IO) {
        socket = Socket(serverIP, port)
        outputStream = socket?.getOutputStream()
        inputStream = socket?.getInputStream()
        
        // 发送握手
        sendCommand("HELLO")
        
        // 启动接收线程
        launch { receiveResponses() }
    }
    
    fun sendCommand(command: String) {
        val data = "$command\n".toByteArray(Charsets.UTF_8)
        outputStream?.write(data)
        outputStream?.flush()
    }
    
    private suspend fun receiveResponses() = withContext(Dispatchers.IO) {
        val reader = BufferedReader(InputStreamReader(inputStream))
        while (true) {
            val response = reader.readLine() ?: break
            handleResponse(response)
        }
    }
    
    fun switchCamera(isFront: Boolean) {
        val camera = if (isFront) "FRONT" else "BACK"
        sendCommand("SWITCH_CAMERA:$camera")
    }
    
    fun toggleFlash(isOn: Boolean) {
        val state = if (isOn) "ON" else "OFF"
        sendCommand("TOGGLE_FLASH:$state")
    }
    
    fun setResolution(width: Int, height: Int) {
        sendCommand("SET_RESOLUTION:$width:$height")
    }
    
    fun setFPS(fps: Int) {
        sendCommand("SET_FPS:$fps")
    }
    
    fun disconnect() {
        socket?.close()
    }
}
```

#### iOS 实现示例
```swift
import Foundation
import Network

class TCPControlClient {
    private var connection: NWConnection?
    private let queue = DispatchQueue(label: "tcp.control")
    
    func connect(host: String, port: UInt16) {
        connection = NWConnection(
            host: NWEndpoint.Host(host),
            port: NWEndpoint.Port(integerLiteral: port),
            using: .tcp
        )
        
        connection?.stateUpdateHandler = { state in
            switch state {
            case .ready:
                self.sendCommand("HELLO")
                self.receiveResponses()
            default:
                break
            }
        }
        
        connection?.start(queue: queue)
    }
    
    func sendCommand(_ command: String) {
        let data = "\(command)\n".data(using: .utf8)!
        connection?.send(content: data, completion: .contentProcessed { error in
            if let error = error {
                print("Send error: \(error)")
            }
        })
    }
    
    private func receiveResponses() {
        connection?.receive(minimumIncompleteLength: 1, maximumLength: 1024) { data, context, isComplete, error in
            if let data = data, let response = String(data: data, encoding: .utf8) {
                self.handleResponse(response)
            }
            if !isComplete {
                self.receiveResponses()
            }
        }
    }
    
    func switchCamera(isFront: Bool) {
        sendCommand("SWITCH_CAMERA:\(isFront ? "FRONT" : "BACK")")
    }
    
    func toggleFlash(isOn: Bool) {
        sendCommand("TOGGLE_FLASH:\(isOn ? "ON" : "OFF")")
    }
}
```

---

### 3. UDP 视频数据协议

#### 数据包格式

每个 UDP 数据包包含一个视频帧的一部分（或完整的小帧）：

##### 选项 A：简单格式（推荐用于 H.264）
```
[Packet Header] + [H.264 NAL Unit]

Packet Header (8 字节):
- 序列号 (4 字节, uint32): 用于检测丢包和重排序
- 时间戳 (4 字节, uint32): 毫秒级时间戳
- 帧类型 (1 字节): 0=I帧, 1=P帧, 2=B帧
- 分片索引 (1 字节): 当前分片序号
- 总分片数 (1 字节): 总分片数量
- 标志位 (1 字节): bit0=关键帧, bit1=最后一片
```

##### 选项 B：完整格式（支持多帧）
```
[Frame Header] + [Slice Header] + [H.264 Data]

Frame Header (12 字节):
- Magic Number (4 字节): 0x56434D46 ('VCMF')
- 序列号 (4 字节)
- 时间戳 (4 字节)

Slice Header (8 字节):
- 帧 ID (2 字节)
- 分片索引 (2 字节)
- 总分片数 (2 字节)
- 数据长度 (2 字节)
```

#### Android 发送示例（使用 MediaCodec）

```kotlin
class VideoStreamSender(
    private val serverIP: String,
    private val udpPort: Int,
    private val width: Int,
    private val height: Int,
    private val fps: Int
) {
    private lateinit var mediaCodec: MediaCodec
    private lateinit var udpSocket: DatagramSocket
    private val serverAddress = InetAddress.getByName(serverIP)
    private var sequenceNumber = 0
    
    fun start() {
        // 初始化 UDP Socket
        udpSocket = DatagramSocket()
        
        // 配置 MediaCodec (H.264 编码器)
        val format = MediaFormat.createVideoFormat(
            MediaFormat.MIMETYPE_VIDEO_AVC,
            width,
            height
        ).apply {
            setInteger(MediaFormat.KEY_COLOR_FORMAT, 
                MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
            setInteger(MediaFormat.KEY_BIT_RATE, 2_000_000) // 2Mbps
            setInteger(MediaFormat.KEY_FRAME_RATE, fps)
            setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1) // 每秒一个关键帧
        }
        
        mediaCodec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        mediaCodec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        
        // 获取 Surface 用于摄像头输入
        val inputSurface = mediaCodec.createInputSurface()
        
        mediaCodec.start()
        
        // 启动输出线程
        Thread { processOutput() }.start()
    }
    
    private fun processOutput() {
        val bufferInfo = MediaCodec.BufferInfo()
        
        while (true) {
            val outputBufferIndex = mediaCodec.dequeueOutputBuffer(bufferInfo, 10000)
            
            if (outputBufferIndex >= 0) {
                val outputBuffer = mediaCodec.getOutputBuffer(outputBufferIndex)
                
                if (outputBuffer != null && bufferInfo.size > 0) {
                    // 读取编码后的 H.264 数据
                    val data = ByteArray(bufferInfo.size)
                    outputBuffer.get(data)
                    
                    // 发送通过 UDP
                    sendH264Data(data, bufferInfo.presentationTimeUs / 1000)
                }
                
                mediaCodec.releaseOutputBuffer(outputBufferIndex, false)
            }
        }
    }
    
    private fun sendH264Data(data: ByteArray, timestampMs: Long) {
        val maxPacketSize = 1400 // MTU 通常是 1500，留 100 字节给包头
        val totalPackets = (data.size + maxPacketSize - 1) / maxPacketSize
        
        for (i in 0 until totalPackets) {
            val offset = i * maxPacketSize
            val length = minOf(maxPacketSize, data.size - offset)
            
            // 构建数据包
            val packet = ByteBuffer.allocate(8 + length)
            packet.putInt(sequenceNumber++)
            packet.putInt(timestampMs.toInt())
            // 这里可以添加更多头部信息
            packet.put(data, offset, length)
            
            // 发送 UDP 包
            val dgram = DatagramPacket(
                packet.array(),
                packet.position(),
                serverAddress,
                udpPort
            )
            udpSocket.send(dgram)
        }
    }
    
    fun stop() {
        mediaCodec.stop()
        mediaCodec.release()
        udpSocket.close()
    }
}
```

#### iOS 发送示例（使用 AVFoundation）

```swift
import AVFoundation
import VideoToolbox
import Network

class VideoStreamSender {
    private var compressionSession: VTCompressionSession?
    private var udpConnection: NWConnection?
    private var sequenceNumber: UInt32 = 0
    
    func start(serverIP: String, port: UInt16, width: Int, height: Int) {
        // 创建 UDP 连接
        udpConnection = NWConnection(
            host: NWEndpoint.Host(serverIP),
            port: NWEndpoint.Port(integerLiteral: port),
            using: .udp
        )
        udpConnection?.start(queue: .global())
        
        // 配置 H.264 编码器
        var session: VTCompressionSession?
        let status = VTCompressionSessionCreate(
            allocator: kCFAllocatorDefault,
            width: Int32(width),
            height: Int32(height),
            codecType: kCMVideoCodecType_H264,
            encoderSpecification: nil,
            imageBufferAttributes: nil,
            compressedDataAllocator: nil,
            outputCallback: encodingOutputCallback,
            refcon: Unmanaged.passUnretained(self).toOpaque(),
            compressionSessionOut: &session
        )
        
        guard status == noErr, let session = session else {
            return
        }
        
        compressionSession = session
        
        // 设置编码参数
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_ProfileLevel, 
            value: kVTProfileLevel_H264_Baseline_AutoLevel)
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_AverageBitRate, 
            value: 2_000_000 as CFNumber)
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_MaxKeyFrameInterval, 
            value: 30 as CFNumber)
        
        VTCompressionSessionPrepareToEncodeFrames(session)
    }
    
    func encodeFrame(_ sampleBuffer: CMSampleBuffer) {
        guard let session = compressionSession,
              let imageBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            return
        }
        
        let presentationTimeStamp = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        
        VTCompressionSessionEncodeFrame(
            session,
            imageBuffer: imageBuffer,
            presentationTimeStamp: presentationTimeStamp,
            duration: .invalid,
            frameProperties: nil,
            sourceFrameRefcon: nil,
            infoFlagsOut: nil
        )
    }
    
    private let encodingOutputCallback: VTCompressionOutputCallback = { 
        refcon, sourceFrameRefcon, status, infoFlags, sampleBuffer in
        
        guard status == noErr, let sampleBuffer = sampleBuffer else {
            return
        }
        
        let sender = Unmanaged<VideoStreamSender>.fromOpaque(refcon!).takeUnretainedValue()
        sender.sendEncodedFrame(sampleBuffer)
    }
    
    private func sendEncodedFrame(_ sampleBuffer: CMSampleBuffer) {
        guard let dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer) else {
            return
        }
        
        var length: Int = 0
        var dataPointer: UnsafeMutablePointer<Int8>?
        CMBlockBufferGetDataPointer(dataBuffer, atOffset: 0, lengthAtOffsetOut: nil, 
            totalLengthOut: &length, dataPointerOut: &dataPointer)
        
        guard let data = dataPointer else {
            return
        }
        
        // 分包发送
        let maxPacketSize = 1400
        let totalPackets = (length + maxPacketSize - 1) / maxPacketSize
        
        for i in 0..<totalPackets {
            let offset = i * maxPacketSize
            let packetLength = min(maxPacketSize, length - offset)
            
            var packet = Data(capacity: 8 + packetLength)
            packet.append(contentsOf: withUnsafeBytes(of: sequenceNumber.bigEndian) { Array($0) })
            sequenceNumber += 1
            
            let timestamp = UInt32(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sampleBuffer)) * 1000)
            packet.append(contentsOf: withUnsafeBytes(of: timestamp.bigEndian) { Array($0) })
            
            packet.append(Data(bytes: data.advanced(by: offset), count: packetLength))
            
            udpConnection?.send(content: packet, completion: .contentProcessed { error in
                if let error = error {
                    print("Send error: \(error)")
                }
            })
        }
    }
    
    func stop() {
        if let session = compressionSession {
            VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid)
            VTCompressionSessionInvalidate(session)
        }
        udpConnection?.cancel()
    }
}
```

---

## ?? 完整的手机端实现流程

### Android 完整示例

```kotlin
class VideoCameraClient(private val context: Context) {
    private var tcpClient: TCPControlClient? = null
    private var videoSender: VideoStreamSender? = null
    private var discoveryListener: DiscoveryListener? = null
    
    // 1. 启动服务发现
    fun startDiscovery() {
        discoveryListener = DiscoveryListener { serverInfo ->
            // 发现服务器后自动连接
            connectToServer(serverInfo)
        }
        discoveryListener?.start()
    }
    
    // 2. 连接到服务器
    private suspend fun connectToServer(serverInfo: ServerInfo) = withContext(Dispatchers.IO) {
        // 连接 TCP 控制通道
        tcpClient = TCPControlClient(serverInfo.ip, serverInfo.tcpPort)
        tcpClient?.connect()
        
        // 启动视频流
        videoSender = VideoStreamSender(
            serverInfo.ip,
            serverInfo.udpPort,
            1920, 1080, 30
        )
        videoSender?.start()
    }
    
    // 3. 控制命令
    fun switchCamera(isFront: Boolean) {
        tcpClient?.switchCamera(isFront)
    }
    
    fun toggleFlash(isOn: Boolean) {
        tcpClient?.toggleFlash(isOn)
    }
    
    // 4. 清理
    fun disconnect() {
        videoSender?.stop()
        tcpClient?.disconnect()
        discoveryListener?.stop()
    }
}
```

### iOS 完整示例

```swift
class VideoCameraClient {
    private var tcpClient: TCPControlClient?
    private var videoSender: VideoStreamSender?
    private var discoveryService: DiscoveryService?
    
    func startDiscovery() {
        discoveryService = DiscoveryService { serverInfo in
            self.connectToServer(serverInfo)
        }
        discoveryService?.start()
    }
    
    func connectToServer(_ serverInfo: ServerInfo) {
        tcpClient = TCPControlClient()
        tcpClient?.connect(host: serverInfo.ip, port: serverInfo.tcpPort)
        
        videoSender = VideoStreamSender()
        videoSender?.start(
            serverIP: serverInfo.ip,
            port: serverInfo.udpPort,
            width: 1920,
            height: 1080
        )
    }
    
    func disconnect() {
        videoSender?.stop()
        tcpClient?.disconnect()
        discoveryService?.stop()
    }
}
```

---

## ?? 服务端接收和解码

你的服务端已经有了接收框架，现在需要添加 H.264 解码。请参考我之前创建的文档中关于集成 FFmpeg 或其他解码器的部分。

---

## ?? 性能优化建议

### 1. UDP 缓冲区大小
```csharp
// 服务端增大接收缓冲区
_udpClient = new UdpClient(UdpPort);
_udpClient.Client.ReceiveBufferSize = 1024 * 1024; // 1MB
```

### 2. 分包策略
- MTU 通常是 1500 字节
- 建议每个 UDP 包不超过 1400 字节
- 大帧需要分片传输

### 3. 丢包处理
- I 帧（关键帧）丢失：等待下一个 I 帧
- P 帧丢失：跳过当前帧，继续解码
- 使用序列号检测丢包

### 4. 延迟优化
- 减少编码器缓冲：使用实时模式
- 禁用 B 帧：减少编码延迟
- 降低 GOP 大小：更频繁的关键帧

---

## ? 总结

你的 TCP + UDP 架构非常适合局域网视频传输：

1. ? **简单高效** - 不需要复杂的 RTSP/WebRTC 库
2. ? **低延迟** - UDP 直接传输，无需额外协议层
3. ? **可控性强** - 可以自定义所有行为
4. ? **已有基础** - 你的框架已经搭建好了

下一步你需要：
1. 在手机端实现 H.264 编码和 UDP 发送
2. 在服务端集成 H.264 解码器（FFmpeg 或 Windows Media Foundation）
3. 测试和优化性能

需要我帮你实现 H.264 解码器集成吗？
