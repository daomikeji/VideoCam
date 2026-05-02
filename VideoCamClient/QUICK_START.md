# 快速开始指南

## 🚀 5分钟快速开始

### 前置条件
- Node.js 20+
- Java JDK 11+（Android开发）
- Xcode 14+（iOS开发）
- React Native CLI

### 第一步：安装依赖

```bash
npm install
```

### 第二步：编译到设备

**Android：**
```bash
npm run android
```

**iOS：**
```bash
cd ios
pod install
cd ..
npm run ios
```

### 第三步：启动C#后端服务器

确保你的C#后端在运行并监听：
- UDP 端口 9001 (视频流)
- TCP 端口 9000 (控制命令)
- 广播 端口 9005 (发现)

### 第四步：测试应用

1. 在你的设备或模拟器上打开应用
2. 应用会自动发现服务器
3. 点击"连接并开始流传输"
4. 测试摄像头切换和闪光灯控制

## 📱 主要功能

### 自动服务器发现
- 应用启动时自动发现C#后端服务器
- 无需手动输入IP地址

### 摄像头控制
- **前置/后置切换**: 点击📷按钮在前置和后置摄像头之间切换
- **闪光灯控制**: 点击⚡按钮开启/关闭闪光灯（仅后置摄像头支持）

### 实时流传输
- H.264编码视频流通过UDP实时发送到服务器
- 支持1280x720分辨率，30fps

## 🔧 配置调整

### 改变视频参数

编辑 Android 模块中的常数：
```kotlin
companion object {
    private const val H264_FRAME_RATE = 30        // 改为15或60
    private const val H264_BIT_RATE = 5000000     // 改为3000000或8000000
    private const val VIDEO_WIDTH = 1280          // 改为1920
    private const val VIDEO_HEIGHT = 720          // 改为1080
}
```

iOS 模块中的常数：
```swift
private let frameRate: Int32 = 30
private let bitRate: Int = 5000000
// 在 H264Encoder.swift 的 setupCompressionSession() 中修改
let width: Int32 = 1280
let height: Int32 = 720
```

### 改变端口号

在 App.tsx 中修改：
```typescript
const DISCOVERY_PORT = 9005;
const [tcpPort, setTcpPort] = useState(9000);
const [udpPort, setUdpPort] = useState(9001);
```

## 🐛 故障排查

### 无法连接到服务器

1. **检查网络连接**
   ```bash
   # Ping服务器IP
   ping 192.168.1.100
   ```

2. **检查防火墙**
   - 确保防火墙允许TCP 9000端口
   - 确保防火墙允许UDP 9001端口

3. **检查服务器状态**
   - 确认C#后端正在运行
   - 检查服务器日志中是否有错误

### 摄像头无法启动

1. **Android**
   - 检查是否已授予CAMERA权限
   - 确认没有其他应用占用摄像头

2. **iOS**
   - 检查Info.plist中是否有NSCameraUsageDescription
   - 在设置中检查应用的摄像头权限

### 性能问题

- 降低视频分辨率和帧率
- 减少比特率
- 检查设备CPU使用率
- 在5GHz Wi-Fi网络上运行

## 📊 监控和调试

### Android 调试

```bash
# 查看原生模块日志
adb logcat | grep VideoStreaming

# 查看所有日志
adb logcat
```

### iOS 调试

- 在Xcode Console中查看日志
- 使用Xcode Debugger设置断点
- 使用Instruments进行性能分析

### 网络监控

**Android：**
- 使用Android Studio的Network Profiler
- 监控TCP/UDP流量

**iOS：**
- 使用Xcode的Network Link Conditioner
- 模拟不同的网络条件

## 📚 相关文档

- [完整集成指南](./INTEGRATION_GUIDE.md) - 详细的技术文档
- [设置说明](./SETUP_INSTRUCTIONS.md) - 平台特定的配置步骤
- [实现总结](./IMPLEMENTATION_SUMMARY.md) - 架构和实现细节
- [C#后端示例](./SERVER_INTEGRATION_EXAMPLE.cs) - 服务器集成代码示例

## 💡 最佳实践

1. **使用稳定的Wi-Fi网络**
   - 避免使用蜂窝网络
   - 选择5GHz频段提高性能

2. **处理权限**
   - 总是检查权限状态
   - 在权限被拒绝时提供友好的错误提示

3. **错误处理**
   - 实现重连机制
   - 记录详细的错误日志

4. **性能优化**
   - 根据网络条件调整编码参数
   - 监控设备资源使用情况
   - 及时释放资源

## 🎯 下一步

1. 定制UI以匹配你的应用设计
2. 实现更多的摄像头控制功能
3. 添加视频录制功能
4. 优化性能以支持更高分辨率
5. 实现错误恢复机制

## 🆘 需要帮助？

### 常见问题

**Q: 如何在不同网络上运行？**
A: 修改App.tsx中的IP地址，或使用DNS名称代替IP地址（需后端支持）。

**Q: 支持录制视频吗？**
A: 当前实现只发送流到服务器。在C#后端中可以接收并保存视频。

**Q: 如何提高性能？**
A: 参见《性能问题》部分。通常降低分辨率和帧率能显著改善性能。

**Q: iOS和Android支持相同的功能吗？**
A: 是的，都支持完整的功能集。只是实现方式不同。

---

✨ 祝你使用愉快！如有任何问题，请参考相关文档。
