# OBS Virtual Camera 集成说明

## 概述
本项目已集成 OBS Virtual Camera，可以将从手机端接收到的视频流推送到虚拟摄像头，供其他应用程序（如 Zoom、Skype、OBS Studio 等）使用。

## 前置要求

1. **OBS Virtual Camera 驱动**
   - 已生成 `obs-virtualsource.dll`
   - 已注册到 Windows 注册表
   - DLL 文件需要放置在应用程序可以访问的位置

2. **DLL 部署位置**
   将 `obs-virtualsource.dll` 放置在以下任一位置：
   - 应用程序的 bin 目录（与 .exe 同级）
   - Windows 系统目录（C:\Windows\System32）
   - PATH 环境变量包含的目录

## 实现细节

### 1. OBS Virtual Camera 互操作层
文件：`Services/OBSVirtualCameraInterop.cs`

提供了与 `obs-virtualsource.dll` 的 P/Invoke 接口：
- `obs_virtual_cam_start()` - 启动虚拟摄像头
- `obs_virtual_cam_stop()` - 停止虚拟摄像头
- `obs_virtual_cam_video(ref VideoFrame)` - 推送视频帧
- `obs_virtual_cam_active()` - 检查摄像头状态

### 2. VirtualCamera 实现
文件：`Services/VirtualCamera.cs`

实现了 `IVirtualCameraSink` 接口，负责：
- 初始化 OBS Virtual Camera
- 将解码后的视频帧推送到虚拟摄像头
- 管理帧时间戳和格式转换
- 正确释放资源

### 3. 视频格式
当前实现使用 **BGRA** 格式（每像素 4 字节）：
- B (Blue): 1 字节
- G (Green): 1 字节
- R (Red): 1 字节
- A (Alpha): 1 字节

如果您的视频源使用其他格式（如 NV12、I420 等），需要进行格式转换。

## 使用流程

### 自动启动
虚拟摄像头会在第一次接收到视频帧时自动初始化：

```csharp
// 在 VideoProcessor 中
private void EnsureVirtualCameraInitialized()
{
    if (_virtualCamera == null)
    {
        _virtualCamera = new VirtualCamera(1920, 1080, 30);
    }
}
```

### 帧推送
每当解码器产生新帧时，会自动推送到虚拟摄像头：

```csharp
public void ProcessUdpPacket(byte[] data)
{
    // 解码逻辑...
    byte[] decodedFrame = new byte[VideoWidth * VideoHeight * 4]; // BGRA
    
    // 确保虚拟摄像头已初始化
    EnsureVirtualCameraInitialized();
    
    // 推送帧到虚拟摄像头
    _virtualCamera?.PushFrame(decodedFrame);
}
```

### 正常关闭
应用程序退出时会自动关闭虚拟摄像头：

```csharp
videoProcessor.Shutdown(); // 会调用 VirtualCamera.Shutdown()
```

## 故障排查

### 1. DLL 找不到错误
```
[VirtualCamera] 错误: 找不到 obs-virtualsource.dll
```

**解决方案：**
- 确认 `obs-virtualsource.dll` 在正确的位置
- 检查 DLL 是否为正确的架构（x64/x86）
- 使用 Dependency Walker 检查依赖项

### 2. 虚拟摄像头启动失败
```
[VirtualCamera] 警告: OBS 虚拟摄像头启动失败
```

**解决方案：**
- 确认虚拟摄像头驱动已正确注册
- 检查注册表：`HKEY_LOCAL_MACHINE\SOFTWARE\OBS\VirtualCam`
- 重新安装/注册虚拟摄像头驱动

### 3. 帧推送失败
```
[VirtualCamera] 错误: 推送帧失败
```

**解决方案：**
- 检查帧数据大小是否正确（width × height × 4 字节）
- 确认视频格式是否为 BGRA
- 检查是否有其他应用程序占用虚拟摄像头

### 4. 其他应用程序看不到虚拟摄像头

**解决方案：**
- 在设备管理器中检查虚拟摄像头设备
- 重启应用程序（如 Zoom、Skype）
- 检查摄像头权限设置

## 测试虚拟摄像头

1. **使用 Windows Camera 应用**
   - 打开 Windows 10/11 相机应用
   - 在设置中选择 "OBS Virtual Camera"
   - 应该能看到推送的视频流

2. **使用其他视频会议软件**
   - Zoom: 设置 → 视频 → 摄像头 → 选择 "OBS Virtual Camera"
   - Skype: 设置 → 音频和视频 → 摄像头 → 选择 "OBS Virtual Camera"
   - Microsoft Teams: 同样在设置中选择虚拟摄像头

## 性能优化建议

1. **帧率控制**
   - 根据实际需求调整 FPS（当前为 30fps）
   - 较低的帧率可以减少 CPU 使用

2. **分辨率优化**
   - 当前默认为 1920x1080
   - 可以根据实际需求降低分辨率

3. **格式转换**
   - 如果视频源不是 BGRA 格式，建议使用硬件加速转换
   - 考虑使用 SIMD 指令优化格式转换

## 扩展功能

如需支持其他视频格式，可在 `VirtualCamera.PushFrame()` 中添加格式转换：

```csharp
// 示例：NV12 转 BGRA
private byte[] ConvertNV12ToBGRA(byte[] nv12Data, int width, int height)
{
    byte[] bgraData = new byte[width * height * 4];
    // 实现 NV12 到 BGRA 的转换逻辑
    return bgraData;
}
```

## 相关资源

- OBS Virtual Camera 源码：https://github.com/obsproject/obs-studio
- OBS Plugin API：https://obsproject.com/docs/
- Virtual Camera Filter SDK：DirectShow API 文档
