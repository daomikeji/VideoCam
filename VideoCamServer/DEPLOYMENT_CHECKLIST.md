# OBS Virtual Camera 部署清单

## ?? 部署步骤

### 1. 准备 OBS Virtual Camera DLL

确保你已经完成以下步骤：

- ? 使用 OBS Virtual Camera 源码编译生成 `obs-virtualsource.dll`
- ? 已注册虚拟摄像头到 Windows 注册表
- ? 在设备管理器中可以看到虚拟摄像头设备

### 2. 部署 DLL 文件

将 `obs-virtualsource.dll` 及其依赖文件复制到以下位置之一：

#### 选项 A：应用程序目录（推荐）
```
D:\job\VideoCam\VideoCamServer\bin\Debug\net5.0-windows\
├── VideoCamServer.exe
├── obs-virtualsource.dll          ← 主要 DLL
└── [其他依赖 DLL]
```

#### 选项 B：系统目录
```
C:\Windows\System32\obs-virtualsource.dll
```

**注意**：确保 DLL 架构与应用程序匹配（都是 x64 或都是 x86）

### 3. 检查依赖项

OBS Virtual Camera 可能依赖以下运行库：

- **Visual C++ Redistributable**
  - 下载：https://aka.ms/vs/17/release/vc_redist.x64.exe
  - 或从 OBS Studio 安装目录复制相关 DLL

- **常见依赖 DLL**：
  - `msvcp140.dll`
  - `vcruntime140.dll`
  - `vcruntime140_1.dll`

使用 [Dependency Walker](https://www.dependencywalker.com/) 检查缺少的依赖项：
```
depends.exe obs-virtualsource.dll
```

### 4. 验证注册表项

打开注册表编辑器 (`regedit`) 并检查：

#### 64位系统
```
HKEY_LOCAL_MACHINE\SOFTWARE\OBS\VirtualCam
或
HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\OBS\VirtualCam
```

应该包含类似以下的键值：
- `InstallPath`: DLL 安装路径
- `Version`: 版本号
- 其他配置项

### 5. 检查虚拟摄像头设备

在设备管理器中验证：

1. 打开"设备管理器" (Win + X → 设备管理器)
2. 展开"相机"或"图像设备"类别
3. 应该能看到 "OBS Virtual Camera" 或类似名称的设备

### 6. 运行应用程序

#### 方式 A：从 Visual Studio 运行
1. 在 Visual Studio 中打开项目
2. 按 F5 或点击"开始调试"
3. 查看控制台输出

#### 方式 B：直接运行 EXE
```powershell
cd D:\job\VideoCam\VideoCamServer\bin\Debug\net5.0-windows\
.\VideoCamServer.exe
```

### 7. 监控日志输出

应用程序启动时，你应该看到类似以下的日志：

```
[Decoder] 正在初始化 H.264 解码器...
[Decoder] 解码器初始化完成。
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

当接收到第一帧时：
```
[VirtualCamera] OBS 虚拟摄像头已启动 (1920x1080 @ 30fps)
```

## ?? 测试虚拟摄像头

### 测试 1：Windows Camera 应用
1. 打开 Windows Camera 应用（开始菜单 → Camera）
2. 点击设置图标
3. 选择 "OBS Virtual Camera"
4. 应该能看到从手机推送的视频流

### 测试 2：Chrome/Edge 浏览器
1. 访问 https://webcamtests.com/
2. 允许摄像头权限
3. 选择 "OBS Virtual Camera"
4. 应该能看到视频流

### 测试 3：视频会议软件
- **Zoom**：设置 → 视频 → 摄像头 → OBS Virtual Camera
- **Skype**：设置 → 音频和视频 → 摄像头 → OBS Virtual Camera
- **Microsoft Teams**：设置 → 设备 → 摄像头 → OBS Virtual Camera
- **Discord**：用户设置 → 语音与视频 → 摄像头 → OBS Virtual Camera

## ?? 常见问题

### 问题 1：找不到 DLL
```
[VirtualCamera] 错误: 找不到 obs-virtualsource.dll
```

**解决方案**：
1. 检查 DLL 是否在正确位置
2. 检查 DLL 架构是否匹配（x64 vs x86）
3. 使用 Process Monitor 查看实际搜索路径
4. 将 DLL 路径添加到 PATH 环境变量

### 问题 2：虚拟摄像头启动失败
```
[VirtualCamera] 警告: OBS 虚拟摄像头启动失败
```

**解决方案**：
1. 以管理员身份运行应用程序
2. 检查注册表项是否正确
3. 重新安装虚拟摄像头驱动
4. 重启计算机

### 问题 3：其他应用看不到虚拟摄像头

**解决方案**：
1. 确认虚拟摄像头在设备管理器中可见
2. 重启需要使用摄像头的应用程序
3. 检查应用程序的摄像头权限（Windows 设置 → 隐私 → 摄像头）
4. 尝试禁用并重新启用设备管理器中的虚拟摄像头

### 问题 4：视频显示黑屏或花屏

**解决方案**：
1. 检查帧数据格式是否正确（应为 BGRA）
2. 检查帧大小是否匹配（width × height × 4）
3. 确认解码器输出正确的数据
4. 使用 `VideoFormatConverter` 进行格式转换

### 问题 5：性能问题（卡顿、延迟）

**解决方案**：
1. 降低分辨率（从 1920x1080 降到 1280x720）
2. 降低帧率（从 30fps 降到 15fps）
3. 使用硬件加速解码
4. 优化格式转换算法

## ?? 代码集成示例

### 示例 1：从 H.264 解码器输出 NV12 格式
```csharp
// 在 VideoProcessor.ProcessUdpPacket 中
byte[] h264Packet = data;
byte[] nv12Frame = H264Decoder.Decode(h264Packet); // 假设返回 NV12

// 转换为 BGRA
byte[] bgraFrame = VideoFormatConverter.ConvertNV12ToBGRA(
    nv12Frame, 
    VideoWidth, 
    VideoHeight
);

// 推送到虚拟摄像头
_virtualCamera?.PushFrame(bgraFrame);
```

### 示例 2：从 FFmpeg 解码器输出 I420 格式
```csharp
// 使用 FFmpeg.AutoGen
AVFrame* frame = // ... FFmpeg 解码输出
byte[] i420Data = ExtractI420FromAVFrame(frame);

// 转换为 BGRA
byte[] bgraFrame = VideoFormatConverter.ConvertI420ToBGRA(
    i420Data, 
    VideoWidth, 
    VideoHeight
);

// 推送到虚拟摄像头
_virtualCamera?.PushFrame(bgraFrame);
```

### 示例 3：动态格式检测
```csharp
byte[] videoData = // ... 从网络或文件读取的数据
string format = VideoFormatConverter.DetectFormat(videoData, VideoWidth, VideoHeight);

byte[] bgraFrame;
switch (format)
{
    case "NV12/I420":
        // 尝试 NV12 转换
        bgraFrame = VideoFormatConverter.ConvertNV12ToBGRA(videoData, VideoWidth, VideoHeight);
        break;
    case "RGB24":
        bgraFrame = VideoFormatConverter.ConvertRGB24ToBGRA(videoData, VideoWidth, VideoHeight);
        break;
    case "BGRA/RGBA":
        // 可能已经是 BGRA，直接使用或转换
        bgraFrame = videoData;
        break;
    default:
        Console.WriteLine($"不支持的格式: {format}");
        return;
}

_virtualCamera?.PushFrame(bgraFrame);
```

## ?? 高级配置

### 自定义分辨率和帧率
```csharp
// 在 VideoProcessor 构造函数中
private const int VideoWidth = 1280;  // 改为 1280x720
private const int VideoHeight = 720;
private const int VideoFps = 15;      // 降低到 15fps

_virtualCamera = new VirtualCamera(VideoWidth, VideoHeight, VideoFps);
```

### 多虚拟摄像头支持
如果需要支持多个虚拟摄像头实例：
```csharp
private VirtualCamera _frontCamera;
private VirtualCamera _backCamera;

_frontCamera = new VirtualCamera(1920, 1080, 30);
_backCamera = new VirtualCamera(1920, 1080, 30);

// 根据摄像头模式推送到不同的虚拟摄像头
if (_cameraMode == "FRONT")
    _frontCamera.PushFrame(frame);
else
    _backCamera.PushFrame(frame);
```

## ?? 参考资源

- **OBS Studio GitHub**: https://github.com/obsproject/obs-studio
- **OBS Virtual Camera Plugin**: https://github.com/obsproject/obs-virtualoutput
- **DirectShow SDK**: https://docs.microsoft.com/en-us/windows/win32/directshow/directshow
- **FFmpeg**: https://ffmpeg.org/
- **Dependency Walker**: https://www.dependencywalker.com/

## ? 验证清单

在部署完成后，请确认：

- [ ] `obs-virtualsource.dll` 在正确位置
- [ ] 所有依赖 DLL 都已安装
- [ ] 注册表项正确配置
- [ ] 虚拟摄像头在设备管理器中可见
- [ ] 应用程序成功启动虚拟摄像头
- [ ] Windows Camera 应用可以看到虚拟摄像头
- [ ] 可以正常推送和查看视频流
- [ ] 其他应用（Zoom/Skype）可以使用虚拟摄像头

## ?? 完成！

如果所有步骤都成功完成，你的应用程序现在应该能够：
1. 接收来自手机端的 H.264 视频流
2. 解码视频流
3. 将解码后的帧推送到 OBS Virtual Camera
4. 在任何支持摄像头的应用程序中使用
