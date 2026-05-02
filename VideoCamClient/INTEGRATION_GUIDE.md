# React Native VideoCam 摄像头流传输集成指南

## 概述

这个项目集成了实时摄像头采集、H.264编码和UDP流传输功能。支持Android和iOS平台。

### 主要功能：
- 📷 摄像头采集与H.264编码
- 🌐 UDP实时流传输
- 🔌 TCP控制命令（摄像头切换、闪光灯控制）
- 🔍 自动服务器发现
- 💡 前置/后置摄像头切换
- ⚡ 闪光灯控制（仅后置摄像头）

## Android 配置

### 1. 权限
已在 `AndroidManifest.xml` 中添加必要权限：
```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

### 2. 运行时权限
Android 6.0+ 需要在运行时请求权限。在你的应用中添加权限请求：

```typescript
import { PermissionsAndroid } from 'react-native';

const requestCameraPermission = async () => {
  try {
    const granted = await PermissionsAndroid.request(
      PermissionsAndroid.PERMISSIONS.CAMERA,
      {
        title: "摄像头权限",
        message: "应用需要访问你的摄像头来进行视频流传输",
        buttonNeutral: "稍后询问",
        buttonNegative: "取消",
        buttonPositive: "确定"
      }
    );
    return granted === PermissionsAndroid.RESULTS.GRANTED;
  } catch (err) {
    console.warn(err);
    return false;
  }
};
```

### 3. 编译
```bash
npm install
npm run android
```

## iOS 配置

### 1. 权限
在 `Info.plist` 中添加摄像头权限：

```xml
<key>NSCameraUsageDescription</key>
<string>应用需要访问你的摄像头来进行视频流传输</string>
<key>NSMicrophoneUsageDescription</key>
<string>应用需要访问你的麦克风</string>
```

### 2. Bridging Header
iOS项目已包含 `VideoCamClient-Bridging-Header.h` 文件，用于连接Swift和React Native。

### 3. 运行时权限
需要在使用摄像头前请求用户权限。使用以下代码：

```typescript
import { Platform } from 'react-native';

const requestCameraPermission = async () => {
  if (Platform.OS === 'ios') {
    // iOS权限由系统自动处理，第一次访问摄像头时会弹出请求
    return true;
  }
  // Android权限处理...
};
```

### 4. 编译
```bash
cd ios
pod install
cd ..
npm run ios
```

## API 使用说明

### VideoStreamingModule

#### startDiscovery(port: number): Promise
自动发现服务器。使用UDP广播在指定端口监听服务器响应。

```typescript
const result = await VideoStreamingModule.startDiscovery(9005);
// result: { success: true, ip: "192.168.1.100", tcp: 9000, udp: 9001 }
```

#### connectAndStream(ip: string, tcpPort: number, udpPort: number): Promise
连接到服务器并开始摄像头流传输。

```typescript
const result = await VideoStreamingModule.connectAndStream("192.168.1.100", 9000, 9001);
// result: { success: true, message: "Connected and streaming" }
```

#### disconnect(): Promise
断开连接并停止流传输。

```typescript
await VideoStreamingModule.disconnect();
```

#### sendControlCommand(command: string): Promise
发送控制命令到服务器。

**支持的命令：**
- `SWITCH_CAMERA:FRONT` - 切换到前置摄像头
- `SWITCH_CAMERA:BACK` - 切换到后置摄像头
- `TOGGLE_FLASH:ON` - 开启闪光灯
- `TOGGLE_FLASH:OFF` - 关闭闪光灯

```typescript
await VideoStreamingModule.sendControlCommand("SWITCH_CAMERA:FRONT");
await VideoStreamingModule.sendControlCommand("TOGGLE_FLASH:ON");
```

## 服务器集成

### C# 后端期望格式

**UDP 流数据：**
- H.264编码的视频帧
- 每帧包含：帧头 + 帧数据
- 推荐分包大小：1400字节/包

**TCP 控制命令：**
```
SWITCH_CAMERA:FRONT
SWITCH_CAMERA:BACK
TOGGLE_FLASH:ON
TOGGLE_FLASH:OFF
```

### 服务器发现格式

客户端在指定端口监听UDP广播响应：
```
<SERVER_IP>,<TCP_PORT>,<UDP_PORT>
```

例如：
```
192.168.1.100,9000,9001
```

## 注意事项

### Android
1. H.264编码使用系统内置的MediaCodec
2. 摄像头权限需要在运行时请求
3. 支持Android 5.0+

### iOS
1. H.264编码使用VideoToolbox框架
2. 需要iOS 11.0+
3. 前置摄像头不支持闪光灯

### 性能建议
1. 视频分辨率：1280x720
2. 帧率：30fps
3. 比特率：5Mbps
4. 建议在稳定的Wi-Fi网络下使用

## 常见问题

### Q: 如何处理摄像头权限被拒绝？
A: 在sendControlCommand之前检查权限状态，或引导用户在系统设置中授予权限。

### Q: 如何优化性能？
A:
- 减少视频分辨率
- 降低帧率
- 减少比特率
- 使用5GHz Wi-Fi

### Q: 支持的最小SDK版本？
A:
- Android: API 21 (Android 5.0)
- iOS: 11.0

## 故障排除

### 连接失败
1. 检查网络连接
2. 确保服务器在线并运行
3. 验证IP地址和端口正确
4. 检查防火墙设置

### 流传输中断
1. 检查网络稳定性
2. 查看设备电池状态
3. 检查摄像头是否被其他应用占用

### 性能问题
1. 降低分辨率和帧率
2. 检查CPU使用率
3. 考虑使用硬件编码加速
