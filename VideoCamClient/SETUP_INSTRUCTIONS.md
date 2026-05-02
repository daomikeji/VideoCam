# iOS 配置步骤

## 1. 项目设置

### 在 Xcode 中配置 Swift Module Interface

1. 打开项目：`ios/VideoCamClient.xcodeproj`
2. 选择 `VideoCamClient` target
3. 进入 `Build Settings`
4. 搜索 `Bridging Header`
5. 设置值为：`VideoCamClient/VideoCamClient-Bridging-Header.h`

### 2. Info.plist 配置

打开 `ios/VideoCamClient/Info.plist`，添加以下权限：

```xml
<key>NSCameraUsageDescription</key>
<string>应用需要访问摄像头来进行视频流传输</string>
<key>NSMicrophoneUsageDescription</key>
<string>应用需要访问麦克风</string>
<key>NSLocalNetworkUsageDescription</key>
<string>应用需要访问本地网络来连接服务器</string>
<key>NSBonjourServices</key>
<array>
  <string>_services._tcp</string>
</array>
```

### 3. 修复 Bridging Header

确保 `VideoCamClient-Bridging-Header.h` 正确配置。该文件应该在 `ios/VideoCamClient/` 目录下，内容如下：

```objc
//
//  VideoCamClient-Bridging-Header.h
//  VideoCamClient
//

#ifndef VideoCamClient_Bridging_Header_h
#define VideoCamClient_Bridging_Header_h

#import <React/RCTBridgeModule.h>

#endif /* VideoCamClient_Bridging_Header_h */
```

### 4. 编译 Swift 代码

1. 在 Xcode 中选择 `File > Add Files to "VideoCamClient"`
2. 添加以下 Swift 文件：
   - `VideoStreamingModule.swift`
   - `CameraManager.swift`
   - `H264Encoder.swift`
   - `StreamingManager.swift`
   - `DiscoveryManager.swift`

3. 确保所有文件都勾选 `VideoCamClient` target

### 5. 配置 Build Phases

1. 进入 target 的 `Build Phases`
2. 确保所有 Swift 文件都在 `Compile Sources` 中

### 6. Swift Language Version

1. 进入 `Build Settings`
2. 搜索 `Swift Language Version`
3. 设置为 `Swift 5` 或更高

## Android 额外配置

### 1. Kotlin 版本

确保项目使用最新的 Kotlin 版本。在 `android/build.gradle` 中检查：

```gradle
ext {
    kotlinVersion = "1.9.10" // 或更高版本
}
```

### 2. 权限检查

在 `MainActivity.kt` 中添加运行时权限检查：

```kotlin
import android.Manifest
import android.content.pm.PackageManager
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

class MainActivity : ReactActivity() {
    private val CAMERA_PERMISSION_CODE = 100

    override fun createReactActivityDelegate(): ReactActivityDelegate {
        // 检查摄像头权限
        if (ContextCompat.checkSelfPermission(
                this,
                Manifest.permission.CAMERA
            ) != PackageManager.PERMISSION_GRANTED
        ) {
            ActivityCompat.requestPermissions(
                this,
                arrayOf(Manifest.permission.CAMERA),
                CAMERA_PERMISSION_CODE
            )
        }

        return DefaultReactActivityDelegate(
            this,
            mainComponentName,
            fabricEnabled
        )
    }
}
```

## 测试

### 1. 启动模拟器/设备

**Android：**
```bash
npm run android
```

**iOS：**
```bash
npm run ios
```

### 2. 测试连接

1. 启动 C# 后端服务器
2. 在设备上运行应用
3. 应用应该能够自动发现服务器
4. 点击"连接并开始流传输"按钮
5. 测试摄像头切换和闪光灯控制

### 3. 调试

**Android 日志：**
```bash
adb logcat | grep VideoStreaming
```

**iOS 日志：**
在 Xcode 的 Console 中查看输出

## 常见编译问题

### Pod install 失败

```bash
cd ios
rm Podfile.lock
pod deintegrate
pod install
cd ..
```

### Swift 编译错误

1. 清理构建目录：`Cmd + Shift + K`
2. 删除 DerivedData：
   ```bash
   rm -rf ~/Library/Developer/Xcode/DerivedData/*
   ```
3. 重新构建项目

### Kotlin 编译错误

```bash
cd android
./gradlew clean
./gradlew build
cd ..
```

## 性能监控

### Android Profiler
1. 在 Android Studio 中打开 Profiler
2. 监控 CPU、内存、网络使用情况

### iOS Instruments
1. 在 Xcode 中选择 `Product > Profile`
2. 使用 Instruments 监控性能

## 部署

### Android APK 生成

```bash
cd android
./gradlew assembleRelease
```

APK 位置：`android/app/build/outputs/apk/release/app-release.apk`

### iOS App 打包

1. 在 Xcode 中选择 `Product > Archive`
2. 使用 App Store Connect 进行分发

## 更新依赖

### 检查依赖版本

```bash
npm outdated
```

### 更新 React Native

```bash
npm install react-native@latest
cd ios && pod update && cd ..
```
