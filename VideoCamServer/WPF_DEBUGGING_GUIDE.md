# WPF 应用程序调试指南

## ?? 问题：Console.ReadKey() 异常

### 错误信息
```
System.InvalidOperationException: "Cannot read keys when either application does not have a console or when console input has been redirected. Try Console.Read."
```

### 原因
WPF 应用程序（`OutputType=WinExe`）默认没有控制台窗口，因此 `Console.ReadKey()` 无法工作。

---

## ? 解决方案

### 方案 1：使用 WPF App 启动（已实施）

项目已配置为使用 `App.xaml` 作为启动点，所有后台服务在 `App.OnStartup()` 中初始化。

**变更内容**：
1. ? 注释掉 `VideoCamServer.csproj` 中的 `<StartupObject>VideoCamServer.Program</StartupObject>`
2. ? 在 `App.xaml.cs` 中添加服务初始化代码
3. ? 移除 `Console.ReadKey()` 调用

**服务生命周期**：
```
应用启动 → App.OnStartup() → 初始化服务
                                 ↓
                          MainWindow 显示
                                 ↓
应用关闭 → App.OnExit() → 清理服务
```

---

## ?? 如何查看控制台输出

即使是 WPF 应用程序（WinExe），你仍然可以查看 `Console.WriteLine()` 的输出。

### 方法 1：在 Visual Studio 中查看输出窗口

1. **打开输出窗口**：
   - 菜单：`视图` → `输出` 或 `View` → `Output`
   - 快捷键：`Ctrl + Alt + O`

2. **选择正确的输出源**：
   - 在输出窗口顶部的下拉菜单中选择 "调试" 或 "Debug"

3. **查看日志**：
   ```
   [App] 正在初始化后台服务...
   [Decoder] 正在初始化 H.264 解码器...
   [Decoder] 解码器初始化完成。
   [NetService] 启动网络监听服务...
   [NetService] TCP 监听已启动在端口: 9000
   [NetService] UDP 接收已启动在端口: 9001
   [Discovery] 发现服务已启动在端口: 9005
   [App] 后台服务初始化完成
   ```

### 方法 2：临时启用控制台窗口（开发/调试）

如果你需要一个真实的控制台窗口，可以临时修改项目配置：

#### 步骤：
1. 修改 `VideoCamServer.csproj`：
   ```xml
   <PropertyGroup>
     <OutputType>Exe</OutputType>  <!-- 从 WinExe 改为 Exe -->
     <TargetFramework>net5.0-windows</TargetFramework>
     <Nullable>enable</Nullable>
     <UseWPF>true</UseWPF>
   </PropertyGroup>
   ```

2. 这样应用会同时显示控制台窗口和 WPF 窗口

3. **注意**：发布时记得改回 `WinExe`，否则用户会看到控制台窗口

### 方法 3：使用 DebugView (Sysinternals)

1. 下载 DebugView：https://docs.microsoft.com/en-us/sysinternals/downloads/debugview
2. 运行 DebugView
3. 在代码中使用 `System.Diagnostics.Debug.WriteLine()` 代替 `Console.WriteLine()`
4. DebugView 会捕获所有调试输出

---

## ?? 调试技巧

### 1. 使用断点

在关键位置设置断点：
- `App.OnStartup()` - 服务初始化
- `NetworkListenerService.StartListeners()` - 网络服务启动
- `VideoProcessor.ProcessUdpPacket()` - 视频数据处理
- `VirtualCamera.PushFrame()` - 帧推送

### 2. 条件断点

右键断点 → 条件 → 添加条件，例如：
```csharp
// 只在接收到特定大小的数据包时中断
data.Length > 1000
```

### 3. 监视窗口

在调试时查看变量：
- 监视窗口（`Ctrl + Alt + W, 1`）
- 即时窗口（`Ctrl + Alt + I`）
- 局部变量窗口（`Ctrl + Alt + V, L`）

### 4. 日志到文件

如果需要持久化日志，可以创建日志类：

```csharp
public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VideoCamServer",
        "logs.txt"
    );

    static Logger()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
    }

    public static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] {message}";
        
        // 写入文件
        File.AppendAllText(LogPath, logMessage + Environment.NewLine);
        
        // 同时输出到调试窗口
        System.Diagnostics.Debug.WriteLine(logMessage);
    }
}
```

使用：
```csharp
Logger.Log("[NetService] TCP 监听已启动");
```

---

## ?? 测试运行

### 启动应用程序

1. **在 Visual Studio 中**：
   - 按 `F5`（调试模式）
   - 或 `Ctrl + F5`（无调试运行）

2. **预期行为**：
   - MainWindow 窗口显示
   - 后台服务自动启动
   - 输出窗口显示日志

3. **验证服务**：
   - 检查输出窗口的启动日志
   - 使用手机端连接测试
   - 监控网络连接

### 关闭应用程序

1. 关闭 MainWindow
2. `App.OnExit()` 会自动清理所有服务
3. 检查输出窗口的关闭日志

---

## ?? 常见问题

### 问题 1：看不到 Console.WriteLine 输出

**解决方案**：
- 确保在输出窗口选择了"调试"源
- 或者使用 `System.Diagnostics.Debug.WriteLine()`

### 问题 2：服务未启动

**检查**：
- 在 `App.OnStartup()` 设置断点
- 查看是否有异常
- 检查输出窗口的错误信息

### 问题 3：应用无法正常退出

**原因**：后台线程未正确关闭

**解决方案**：
- 确保 `App.OnExit()` 调用所有清理方法
- 检查 `CancellationToken` 是否正确传递

### 问题 4：仍想使用 Program.Main()

如果必须使用 `Program.Main()` 作为入口点：

```csharp
[STAThread]
public static void Main(string[] args)
{
    // 初始化服务...
    
    // 启动 WPF 应用
    var app = new App();
    app.InitializeComponent();
    app.Run(new MainWindow());
    
    // 不要使用 Console.ReadKey()
}
```

---

## ?? 性能监控

### 在 WPF 中显示状态

可以在 MainWindow 中添加状态显示：

```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    InitializeComponent();
    
    // 订阅服务状态（从 App 获取）
    var app = (App)Application.Current;
    if (app.StateManager != null)
    {
        app.StateManager.StatusUpdated += status =>
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = status;
                LogListBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {status}");
            });
        };
    }
}
```

### 实时日志显示

在 MainWindow.xaml 中添加：
```xml
<ListBox x:Name="LogListBox" 
         Height="200" 
         ScrollViewer.VerticalScrollBarVisibility="Auto"/>
```

---

## ? 当前配置总结

### 启动流程
```
1. App.xaml.cs OnStartup()
   ├── 初始化 ServerStateManager
   ├── 初始化 VideoProcessor
   ├── 启动 NetworkListenerService
   └── 启动 DiscoveryService

2. MainWindow 显示

3. 等待用户操作或手机端连接

4. App.xaml.cs OnExit()
   ├── 停止 NetworkListenerService
   ├── 停止 DiscoveryService
   └── 关闭 VideoProcessor
```

### 服务状态
- ? TCP 监听：端口 9000
- ? UDP 接收：端口 9001
- ? 服务发现：端口 9005
- ? 虚拟摄像头：OBS Virtual Camera

### 日志输出
- ? Visual Studio 输出窗口
- ? 可选：文件日志
- ? 可选：UI 实时显示

---

## ?? 最佳实践

1. **开发阶段**：
   - 使用 Visual Studio 输出窗口
   - 设置断点调试
   - 使用条件断点减少中断

2. **测试阶段**：
   - 启用文件日志
   - 在 UI 中显示关键状态
   - 记录错误和异常

3. **发布版本**：
   - 使用结构化日志库（如 Serilog）
   - 实现错误报告机制
   - 提供诊断信息导出功能

---

## ?? 参考资源

- [WPF 应用程序模型](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/app-development/application-management-overview)
- [调试 WPF 应用](https://docs.microsoft.com/en-us/visualstudio/debugger/debugging-wpf)
- [System.Diagnostics.Debug](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.debug)

---

**问题已解决！** ??

现在你的 WPF 应用可以正常启动和调试，不会再出现 `Console.ReadKey()` 异常。
