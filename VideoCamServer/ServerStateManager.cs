using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoCamServer.Services;

namespace VideoCamServer
{

    // =================================================================================
    // 1. 状态管理 (ServerStateManager)
    // =================================================================================

    /// <summary>
    /// 服务器状态管理器，用于反映并通知 GUI 应用程序的当前状态。
    /// WPF 应用程序的 ViewModel 可以订阅这些事件。
    /// </summary>
    public class ServerStateManager
    {
        public event Action<string> StatusUpdated;
        public event Action<bool> ClientConnectionChanged;

        private bool _isClientConnected = false;
        private string _cameraMode = "BACK"; // BACK | FRONT
        private bool _isFlashOn = false;

        public bool IsClientConnected
        {
            get => _isClientConnected;
            set
            {
                if (_isClientConnected != value)
                {
                    _isClientConnected = value;
                    // 注意：在实际 WPF 应用中，事件触发需要在 UI 线程上，
                    // 但在这里我们先简单地触发事件。
                    StatusUpdated?.Invoke(value ? "客户端已连接" : "客户端已断开");
                    ClientConnectionChanged?.Invoke(value);

                    // 重置控制状态
                    if (!value)
                    {
                        _cameraMode = "BACK";
                        _isFlashOn = false;
                        StatusUpdated?.Invoke("已重置摄像头和闪光灯状态");
                    }
                }
            }
        }

        public void UpdateCameraMode(string mode)
        {
            if (_cameraMode != mode)
            {
                _cameraMode = mode;
                StatusUpdated?.Invoke($"摄像头模式已切换至: {mode}");
            }
        }

        public void UpdateFlashStatus(bool isOn)
        {
            if (_isFlashOn != isOn)
            {
                _isFlashOn = isOn;
                StatusUpdated?.Invoke($"闪光灯已 {(isOn ? "开启" : "关闭")}");
            }
        }
    }


    // =================================================================================
    // 2. 视频处理与解码 (VideoProcessor)
    // =================================================================================

    public delegate void FrameReadyEventHandler(byte[] frameData, int width, int height);

    /// <summary>
    /// 概念性视频处理器：负责接收 UDP 数据、解码 H.264，并管理解码后的帧。
    /// </summary>
    public class VideoProcessor
    {
        private const int VideoWidth = 1920;
        private const int VideoHeight = 1080;

        public event FrameReadyEventHandler FrameReady;

        private IntPtr _ffmpegDecoderContext = IntPtr.Zero;
        private VirtualCamera _virtualCamera;
        private readonly object _cameraLock = new object();

        public VideoProcessor()
        {
            InitializeDecoder();
        }

        private void InitializeDecoder()
        {
            Console.WriteLine("[Decoder] 正在初始化 H.264 解码器...");
            _ffmpegDecoderContext = new IntPtr(1);
            Console.WriteLine("[Decoder] 解码器初始化完成。");
        }

        private void EnsureVirtualCameraInitialized()
        {
            if (_virtualCamera == null)
            {
                lock (_cameraLock)
                {
                    if (_virtualCamera == null)
                    {
                        // Initialize the virtual camera with the same resolution and a default FPS of 30
                        _virtualCamera = new VirtualCamera(VideoWidth, VideoHeight, 30);
                    }
                }
            }
        }

        public void ProcessUdpPacket(byte[] data)
        {
            if (_ffmpegDecoderContext == IntPtr.Zero) return;

            try
            {
                // 模拟解码逻辑：每收到 50 个 UDP 包后，就产生一个解码帧
                if (data.Length > 100 && data[5] % 50 == 0)
                {
                    // 假设这是解码后得到的帧数据
                    // 在实际应用中，这里应该是 H.264 解码器的输出
                    
                    byte[] decodedFrame;
                    
                    // 示例：如果解码器输出的是 NV12 格式，需要转换为 BGRA
                    // byte[] nv12Frame = DecodeH264ToNV12(data);
                    // decodedFrame = VideoFormatConverter.ConvertNV12ToBGRA(nv12Frame, VideoWidth, VideoHeight);
                    
                    // 示例：如果解码器输出的是 I420 格式
                    // byte[] i420Frame = DecodeH264ToI420(data);
                    // decodedFrame = VideoFormatConverter.ConvertI420ToBGRA(i420Frame, VideoWidth, VideoHeight);
                    
                    // 目前使用模拟数据（BGRA 格式）
                    decodedFrame = new byte[VideoWidth * VideoHeight * 4]; // BGRA format: 4 bytes per pixel
                    
                    // 触发帧就绪事件（用于 UI 预览等）
                    FrameReady?.Invoke(decodedFrame, VideoWidth, VideoHeight);

                    // Ensure virtual camera is initialized before pushing frame
                    EnsureVirtualCameraInitialized();
                    
                    // Push the frame to the virtual camera
                    _virtualCamera?.PushFrame(decodedFrame);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Decoder Error] 解码处理失败: {ex.Message}");
            }
        }

        public void Shutdown()
        {
            if (_ffmpegDecoderContext != IntPtr.Zero)
            {
                Console.WriteLine("[Decoder] 正在关闭解码器并释放资源...");
                _ffmpegDecoderContext = IntPtr.Zero;
            }

            _virtualCamera?.Shutdown();
        }
    }


    // =================================================================================
    // 3. 服务发现 (DiscoveryService)
    // =================================================================================

    public class DiscoveryService
    {
        public const int DiscoveryPort = 9005;
        private readonly UdpClient _udpClient;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly int _tcpPort;
        private readonly int _udpPort;

        public DiscoveryService(int tcpPort, int udpPort)
        {
            _tcpPort = tcpPort;
            _udpPort = udpPort;
            _cancellationTokenSource = new CancellationTokenSource();
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;
        }

        public void StartBroadcast()
        {
            Task.Run(() => BroadcastLoopAsync(_cancellationTokenSource.Token));
            Console.WriteLine($"[Discovery] 发现服务已启动在端口: {DiscoveryPort}");
        }

        private async Task BroadcastLoopAsync(CancellationToken token)
        {
            // 广播消息格式: VCAM|TCP_PORT|UDP_PORT
            string messageTemplate = $"VCAM|{_tcpPort}|{_udpPort}";
            byte[] messageBytes = Encoding.ASCII.GetBytes(messageTemplate);

            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _udpClient.SendAsync(messageBytes, messageBytes.Length, broadcastEndpoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Discovery Error] 广播失败: {ex.Message}");
                }

                await Task.Delay(2000, token);
            }
        }

        public void StopBroadcast()
        {
            _cancellationTokenSource.Cancel();
            _udpClient?.Close();
        }
    }


    // =================================================================================
    // 4. 网络监听 (NetworkListenerService)
    // =================================================================================

    /// <summary>
    /// 网络监听服务：处理 TCP 信令连接和 UDP 视频数据接收。
    /// </summary>
    public partial class NetworkListenerService
    {
        public const int TcpPort = 9000;
        public const int UdpPort = 9001;

        private TcpListener _tcpListener;
        private UdpClient _udpClient;
        private CancellationTokenSource _cancellationTokenSource;

        private readonly VideoProcessor _videoProcessor;
        private readonly ServerStateManager _stateManager;

        public NetworkListenerService(VideoProcessor processor, ServerStateManager stateManager)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _videoProcessor = processor;
            _stateManager = stateManager;
        }

        public void StartListeners()
        {
            Console.WriteLine("[NetService] 启动网络监听服务...");

            Task.Run(() => StartTcpListenerAsync(_cancellationTokenSource.Token));
            Task.Run(() => StartUdpReceiverAsync(_cancellationTokenSource.Token));

            Console.WriteLine($"[NetService] TCP 监听已启动在端口: {TcpPort}");
            Console.WriteLine($"[NetService] UDP 接收已启动在端口: {UdpPort}");
        }

        private async Task StartTcpListenerAsync(CancellationToken token)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, TcpPort);
                _tcpListener.Start();

                while (!token.IsCancellationRequested)
                {
                    var clientTask = _tcpListener.AcceptTcpClientAsync();
                    if (await Task.WhenAny(clientTask, Task.Delay(-1, token)) == clientTask)
                    {
                        TcpClient client = await clientTask;
                        _stateManager.IsClientConnected = true; // 更新连接状态
                        Console.WriteLine($"[TCP] 客户端已连接: {((IPEndPoint)client.Client.RemoteEndPoint).Address}");
                        _ = HandleTcpClientAsync(client, token);
                    }
                }
            }
            catch (OperationCanceledException) { Console.WriteLine("[TCP] 监听服务已取消。"); }
            catch (Exception ex) { Console.WriteLine($"[TCP] 发生错误: {ex.Message}"); }
        }

        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    byte[] buffer = new byte[1024];
                    while (!token.IsCancellationRequested)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0) break; // 客户端断开连接

                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Console.WriteLine($"[TCP 接收] 收到控制命令: {message}");

                        // 解析并处理控制命令
                        ProcessControlCommand(message);

                        // 假设性回复（真实应用中会回复状态）
                        string response = "ACK";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                    }
                }
                catch (OperationCanceledException) { /* 正常取消 */ }
                catch (Exception ex) { Console.WriteLine($"[TCP 处理] 客户端连接中断或发生错误: {ex.Message}"); }
                finally
                {
                    _stateManager.IsClientConnected = false; // 更新断开状态
                }
            }
            Console.WriteLine($"[TCP] 客户端断开连接。");
        }

        /// <summary>
        /// 处理来自移动端的控制命令
        /// </summary>
        private void ProcessControlCommand(string command)
        {
            if (command.StartsWith("SWITCH_CAMERA:"))
            {
                string mode = command.Split(':')[1];
                if (mode == "FRONT" || mode == "BACK")
                {
                    _stateManager.UpdateCameraMode(mode);
                }
            }
            else if (command.StartsWith("TOGGLE_FLASH:"))
            {
                string status = command.Split(':')[1];
                bool isOn = status == "ON";
                _stateManager.UpdateFlashStatus(isOn);
            }
            // ... 其他控制命令，如分辨率、帧率等
        }

        private async Task StartUdpReceiverAsync(CancellationToken token)
        {
            try
            {
                _udpClient = new UdpClient(UdpPort);
                while (!token.IsCancellationRequested)
                {
                    var receiveTask = _udpClient.ReceiveAsync();
                    if (await Task.WhenAny(receiveTask, Task.Delay(-1, token)) == receiveTask)
                    {
                        var result = await receiveTask;
                        _videoProcessor.ProcessUdpPacket(result.Buffer);
                    }
                }
            }
            catch (SocketException ex) when (ex.ErrorCode == 10004) { Console.WriteLine("[UDP] 监听服务已关闭。"); }
            catch (OperationCanceledException) { Console.WriteLine("[UDP] 监听服务已取消。"); }
            catch (Exception ex) { Console.WriteLine($"[UDP] 发生错误: {ex.Message}"); }
            finally { _udpClient?.Close(); }
        }

        public void StopListeners()
        {
            Console.WriteLine("[NetService] 正在停止网络监听服务...");
            _cancellationTokenSource.Cancel();
            _tcpListener?.Stop();
            // 确保 UdpClient 释放端口
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient.Dispose();
            }
            Console.WriteLine("[NetService] 网络监听服务已停止。");
        }
    }


    // =================================================================================
    // 5. 程序入口与 UI 模拟 (Program) - 仅用于测试/示例
    // =================================================================================

    /// <summary>
    /// 示例程序入口（已废弃 - 仅供参考）
    /// 实际应用使用 App.xaml.cs 作为启动点
    /// </summary>
    public class Program_Example_DO_NOT_USE
    {
        // C++ 驱动互操作的存根
        private static class DriverInterop
        {
            private const string DllName = "VirtualCamDriver.dll";

            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void PushNewFrame(IntPtr pFrameData, int width, int height);
        }

        // 注意：此方法已不再使用，实际启动在 App.xaml.cs 中
        // [STAThread]
        public static void Main_Example(string[] args)
        {
            // 1. 初始化状态管理器 (WPF 的 ViewModel 实例)
            var stateManager = new ServerStateManager();

            // --- 模拟 WPF UI 线程订阅事件 ---
            // 在实际 WPF 中，这些事件处理函数会使用 Dispatcher.Invoke 来确保在 UI 线程上更新界面。
            stateManager.StatusUpdated += status =>
            {
                // 模拟 WPF Log 窗口更新
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WPF Log] 状态更新: {status}");
                Console.ResetColor();
            };

            stateManager.ClientConnectionChanged += isConnected =>
            {
                // 模拟 WPF 连接状态指示灯更新
                Console.ForegroundColor = isConnected ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"[WPF UI] 连接状态指示灯: {(isConnected ? "🟢 ONLINE" : "🔴 OFFLINE")}");
                Console.ResetColor();
                // 模拟启用/禁用控制面板
            };
            // ----------------------------------

            // 2. 初始化核心服务 (作为后台任务)
            var videoProcessor = new VideoProcessor();
            videoProcessor.FrameReady += OnFrameReady;

            var listener = new NetworkListenerService(videoProcessor, stateManager);
            var discoveryService = new DiscoveryService(
                NetworkListenerService.TcpPort,
                NetworkListenerService.UdpPort
            );

            // 3. 启动所有服务
            listener.StartListeners();
            discoveryService.StartBroadcast();

            // 4. 模拟 WPF 窗口启动后的状态显示
            DisplayInitialUiState();

            // 注意：不能在 WinExe 应用中使用 Console.ReadKey()
            // Console.ReadKey();

            // 5. 清理
            Console.WriteLine("\n[程序退出] 正在停止所有服务...");
            listener.StopListeners();
            discoveryService.StopBroadcast();
            videoProcessor.Shutdown();
            Console.WriteLine("[程序退出] 程序退出。");
        }

        private static void DisplayInitialUiState()
        {
            Console.WriteLine("\n==============================================");
            Console.WriteLine($"   iVCam 桌面端服务器 (WPF 后台) 状态");
            Console.WriteLine("==============================================");
            Console.WriteLine($" - 视频处理端口 (UDP): {NetworkListenerService.UdpPort}");
            Console.WriteLine($" - 控制信令端口 (TCP): {NetworkListenerService.TcpPort}");
            Console.WriteLine($" - 发现广播端口 (UDP): {DiscoveryService.DiscoveryPort}");
            Console.WriteLine("==============================================");
            Console.WriteLine("WPF UI 正在等待连接...");
        }

        private static void OnFrameReady(byte[] frameData, int width, int height)
        {
            // P/Invoke 调用 C++ 驱动推送帧的逻辑
            GCHandle pinnedArray = GCHandle.Alloc(frameData, GCHandleType.Pinned);
            IntPtr pointer = pinnedArray.AddrOfPinnedObject();

            try
            {
                DriverInterop.PushNewFrame(pointer, width, height);
                // 在实际 WPF 中，视频帧会被传递给一个图像控件，如 ImageSource 或 Bitmap
                // Console.WriteLine($"[P/Invoke] 已推送新帧 ({frameData.Length} 字节) 到驱动。");
            }
            catch (DllNotFoundException)
            {
                // 忽略找不到 Dll 的预期错误
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P/Invoke Error] 调用 C++ 函数时发生错误: {ex.Message}");
            }
            finally
            {
                pinnedArray.Free();
            }
        }
    }
}
