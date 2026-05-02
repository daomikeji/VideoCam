# ?? 快速开始指南

## ?? 概述

本指南帮助你快速设置手机端到服务端的视频传输系统。

---

## ?? 系统架构

```
┌─────────────────┐         ┌─────────────────────┐         ┌─────────────────┐
│   手机端         │         │   Windows 服务端     │         │  其他应用        │
│  (Android/iOS)  │         │   (VideoCamServer)  │         │ (Zoom/Skype)    │
├─────────────────┤         ├─────────────────────┤         ├─────────────────┤
│                 │         │                     │         │                 │
│ 摄像头采集       │         │  UDP 接收器         │         │                 │
│      ↓          │         │       ↓            │         │                 │
│ H.264 编码      │ ═══UDP══?  H.264 解码器      │         │                 │
│      ↓          │  9001   │       ↓            │         │                 │
│ UDP 发送        │         │  格式转换 (BGRA)    │         │                 │
│                 │         │       ↓            │         │  读取虚拟摄像头  │
│ TCP 控制        │ ═══TCP══?  虚拟摄像头推送     │ ═══════?│  显示视频       │
│ (命令发送)      │  9000   │  (OBS Virtual Cam) │         │                 │
│                 │         │                     │         │                 │
│ UDP 服务发现    │ ?══UDP══?  广播服务信息      │         │                 │
│                 │  9005   │                     │         │                 │
└─────────────────┘         └─────────────────────┘         └─────────────────┘

工作流程:
1. 服务端广播服务信息 (UDP 9005)
2. 手机端发现并连接服务器 (TCP 9000)
3. 手机端推送 H.264 视频流 (UDP 9001)
4. 服务端解码并推送到虚拟摄像头
5. 其他应用使用虚拟摄像头
```

---

## ?? 服务端设置

### 步骤 1: 部署 OBS Virtual Camera DLL

1. 确保已生成 `obs-virtualsource.dll`
2. 复制到项目输出目录：
   ```
   D:\job\VideoCam\VideoCamServer\bin\Debug\net5.0-windows\obs-virtualsource.dll
   ```

### 步骤 2: 安装 FFmpeg (可选 - 用于 H.264 解码)

1. 安装 NuGet 包：
   ```powershell
   dotnet add package FFmpeg.AutoGen
   ```

2. 下载 FFmpeg DLL:
   - 访问: https://github.com/BtbN/FFmpeg-Builds/releases
   - 下载: `ffmpeg-n6.0-latest-win64-gpl-shared-6.0.zip`
   - 解压并复制以下文件到 `bin\Debug\net5.0-windows\ffmpeg\`:
     - avcodec-60.dll
     - avformat-60.dll
     - avutil-58.dll
     - swscale-7.dll
     - swresample-4.dll

### 步骤 3: 运行服务器

```powershell
cd D:\job\VideoCam\VideoCamServer
dotnet run
```

或在 Visual Studio 中按 F5。

### 预期输出：

```
[VideoProcessor] 正在初始化 H.264 解码器...
[VideoProcessor] H.264 解码器初始化完成。
[NetService] 启动网络监听服务...
[NetService] TCP 监听已启动在端口: 9000
[NetService] UDP 接收已启动在端口: 9001
[Discovery] 发现服务已启动在端口: 9005

==============================================
   iVCam 桌面端服务器 (WPF 后台) 状态
==============================================
 - 视频处理端口 (UDP): 9001
 - 控制信令端口 (TCP): 9000
 - 发现广播端口 (UDP): 9005
==============================================
WPF UI 正在等待连接...
```

---

## ?? 手机端实现

### Android 实现要点

#### 1. 添加权限 (AndroidManifest.xml)

```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.RECORD_AUDIO" />
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
```

#### 2. 主要类结构

```kotlin
// 1. 服务发现
class DiscoveryService {
    fun startListening(onServerFound: (ServerInfo) -> Unit)
}

// 2. TCP 控制客户端
class TCPControlClient(serverIP: String, port: Int) {
    suspend fun connect()
    fun sendCommand(command: String)
    fun switchCamera(isFront: Boolean)
    fun toggleFlash(isOn: Boolean)
}

// 3. 视频流发送器
class VideoStreamSender(serverIP: String, port: Int) {
    fun start()
    fun encodeAndSend(image: Image)
    fun stop()
}

// 4. 主控制器
class VideoCameraClient {
    fun startDiscovery()
    fun connectToServer(serverInfo: ServerInfo)
    fun startStreaming()
    fun disconnect()
}
```

#### 3. 最小可运行代码

```kotlin
// MainActivity.kt
class MainActivity : AppCompatActivity() {
    private lateinit var client: VideoCameraClient
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        
        // 请求权限
        ActivityCompat.requestPermissions(
            this,
            arrayOf(Manifest.permission.CAMERA, Manifest.permission.INTERNET),
            100
        )
        
        client = VideoCameraClient(this)
        
        // 启动服务发现
        findViewById<Button>(R.id.btnConnect).setOnClickListener {
            client.startDiscovery()
        }
        
        // 开始推流
        findViewById<Button>(R.id.btnStart).setOnClickListener {
            client.startStreaming()
        }
    }
    
    override fun onDestroy() {
        super.onDestroy()
        client.disconnect()
    }
}
```

### iOS 实现要点

#### 1. Info.plist 权限

```xml
<key>NSCameraUsageDescription</key>
<string>需要访问相机来进行视频传输</string>
<key>NSLocalNetworkUsageDescription</key>
<string>需要访问本地网络来连接服务器</string>
```

#### 2. 主要类结构

```swift
// 1. 服务发现
class DiscoveryService {
    func startListening(onServerFound: @escaping (ServerInfo) -> Void)
}

// 2. TCP 控制客户端
class TCPControlClient {
    func connect(host: String, port: UInt16)
    func sendCommand(_ command: String)
}

// 3. 视频流发送器
class VideoStreamSender {
    func start(serverIP: String, port: UInt16)
    func encodeAndSend(sampleBuffer: CMSampleBuffer)
}

// 4. 主控制器
class VideoCameraClient {
    func startDiscovery()
    func connectToServer(_ serverInfo: ServerInfo)
    func startStreaming()
}
```

#### 3. 最小可运行代码

```swift
// ViewController.swift
import UIKit
import AVFoundation

class ViewController: UIViewController {
    private var client: VideoCameraClient?
    
    override func viewDidLoad() {
        super.viewDidLoad()
        
        client = VideoCameraClient()
        
        // 请求权限
        AVCaptureDevice.requestAccess(for: .video) { granted in
            if granted {
                DispatchQueue.main.async {
                    self.setupUI()
                }
            }
        }
    }
    
    @IBAction func connectButtonTapped(_ sender: UIButton) {
        client?.startDiscovery()
    }
    
    @IBAction func startButtonTapped(_ sender: UIButton) {
        client?.startStreaming()
    }
}
```

---

## ?? 测试流程

### 1. 测试服务发现

#### 在手机端:
```kotlin
// Android
val socket = DatagramSocket(9005)
socket.broadcast = true
val buffer = ByteArray(1024)
val packet = DatagramPacket(buffer, buffer.size)
socket.receive(packet)
val message = String(packet.data, 0, packet.length)
println("收到: $message") // 应该是 "VCAM|9000|9001"
```

#### 预期结果:
```
收到广播: VCAM|9000|9001
服务器 IP: 192.168.1.100
```

### 2. 测试 TCP 连接

```kotlin
val socket = Socket("192.168.1.100", 9000)
socket.getOutputStream().write("HELLO\n".toByteArray())
val response = socket.getInputStream().read()
println("服务器响应: $response") // 应该是 "ACK"
```

#### 服务端日志:
```
[TCP] 客户端已连接: 192.168.1.50
[TCP 接收] 收到控制命令: HELLO
```

### 3. 测试 UDP 视频传输

```kotlin
val socket = DatagramSocket()
val testData = ByteArray(1400) { 0x00 }
val packet = DatagramPacket(
    testData,
    testData.size,
    InetAddress.getByName("192.168.1.100"),
    9001
)
socket.send(packet)
println("已发送测试包")
```

#### 服务端日志:
```
[UDP] 收到数据包: 1400 字节
[VideoProcessor] 处理 UDP 包...
```

### 4. 测试虚拟摄像头

1. 打开 Windows Camera 应用
2. 在设置中选择 "OBS Virtual Camera"
3. 应该能看到视频流（如果手机端正在推流）

---

## ?? 故障排查

### 问题 1: 手机端无法发现服务器

**症状**: 广播接收超时

**检查项**:
- [ ] 手机和电脑在同一局域网
- [ ] 防火墙允许 UDP 9005 端口
- [ ] 服务端正在运行并广播
- [ ] 手机端正确监听 UDP 广播

**解决方案**:
```powershell
# Windows 防火墙添加规则
netsh advfirewall firewall add rule name="VideoCam Discovery" dir=in action=allow protocol=UDP localport=9005
netsh advfirewall firewall add rule name="VideoCam TCP" dir=in action=allow protocol=TCP localport=9000
netsh advfirewall firewall add rule name="VideoCam UDP" dir=in action=allow protocol=UDP localport=9001
```

### 问题 2: TCP 连接失败

**症状**: Connection refused

**检查项**:
- [ ] 服务端 TCP 监听器已启动
- [ ] 端口 9000 未被占用
- [ ] IP 地址正确
- [ ] 防火墙允许连接

**测试**:
```powershell
# 测试端口是否监听
netstat -an | findstr "9000"
```

### 问题 3: 视频无法显示

**症状**: 虚拟摄像头黑屏

**检查项**:
- [ ] UDP 数据正在接收
- [ ] H.264 解码器正常工作
- [ ] 虚拟摄像头已启动
- [ ] 帧数据格式正确 (BGRA)

**日志检查**:
```
[VideoProcessor] 处理 UDP 包...
[H264Decoder] 解码成功
[VirtualCamera] 推送帧: 1920x1080
```

### 问题 4: 延迟过高

**原因**: 网络拥塞或编码设置不当

**优化方案**:
1. 降低分辨率: 1920x1080 → 1280x720
2. 降低帧率: 30fps → 15fps
3. 调整比特率: 2Mbps → 1Mbps
4. 使用 5GHz WiFi 而非 2.4GHz

---

## ?? 性能基准

### 推荐配置

| 场景 | 分辨率 | 帧率 | 比特率 | 延迟 |
|------|--------|------|--------|------|
| 高质量 | 1920x1080 | 30fps | 2-4Mbps | <100ms |
| 标准 | 1280x720 | 30fps | 1-2Mbps | <80ms |
| 低延迟 | 640x480 | 15fps | 500Kbps | <50ms |

### 网络要求

- **局域网**: 推荐 100Mbps 以上
- **WiFi**: 5GHz 优先，信号强度 >-50dBm
- **丢包率**: <1%

---

## ?? 学习资源

### 文档
- `TCP_UDP_PROTOCOL.md` - 详细的通信协议规范
- `H264_DECODER_INTEGRATION.md` - H.264 解码器集成指南
- `OBS_VIRTUAL_CAMERA_README.md` - 虚拟摄像头使用说明
- `DEPLOYMENT_CHECKLIST.md` - 完整部署清单

### 示例代码
- `Examples/VirtualCameraUsageExample.cs` - C# 示例
- 手机端示例在 `TCP_UDP_PROTOCOL.md` 中

---

## ? 完成清单

### 服务端
- [ ] OBS Virtual Camera DLL 已部署
- [ ] FFmpeg DLL 已部署（如使用）
- [ ] 服务器可以启动
- [ ] UDP 广播正常
- [ ] TCP/UDP 端口监听正常
- [ ] 虚拟摄像头可见

### 手机端
- [ ] 权限已配置
- [ ] 服务发现功能实现
- [ ] TCP 控制客户端实现
- [ ] H.264 编码器配置
- [ ] UDP 发送器实现
- [ ] 摄像头预览正常

### 测试
- [ ] 服务发现成功
- [ ] TCP 连接成功
- [ ] 命令发送/接收正常
- [ ] 视频流传输正常
- [ ] 虚拟摄像头输出正常
- [ ] 其他应用可以使用虚拟摄像头

---

## ?? 下一步

1. **优化性能**: 根据实际情况调整参数
2. **添加功能**: 音频传输、录制等
3. **改进 UI**: 更好的用户界面
4. **错误处理**: 更完善的异常处理
5. **测试**: 各种网络环境下测试

---

## ?? 需要帮助？

参考以下文档获取详细信息：

- **协议规范**: `TCP_UDP_PROTOCOL.md`
- **解码器**: `H264_DECODER_INTEGRATION.md`
- **虚拟摄像头**: `OBS_VIRTUAL_CAMERA_README.md`
- **部署**: `DEPLOYMENT_CHECKLIST.md`
- **总结**: `INTEGRATION_SUMMARY.md`

祝你开发顺利！??
