using System;
using System.Collections.Generic;
using System.Linq;
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
        public event Action<string> ClientIpChanged;
        public event Action<string> CameraModeChanged;
        public event Action<bool> FlashStatusChanged;

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
                        ClientIpChanged?.Invoke("无连接");
                        CameraModeChanged?.Invoke(_cameraMode);
                        FlashStatusChanged?.Invoke(_isFlashOn);
                        StatusUpdated?.Invoke("已重置摄像头和闪光灯状态");
                    }
                }
            }
        }

        public void UpdateClientIp(string ip)
        {
            ClientIpChanged?.Invoke(string.IsNullOrWhiteSpace(ip) ? "无连接" : ip);
        }

        public void UpdateCameraMode(string mode)
        {
            if (_cameraMode != mode)
            {
                _cameraMode = mode;
                CameraModeChanged?.Invoke(mode);
                StatusUpdated?.Invoke($"摄像头模式已切换至: {mode}");
            }
        }

        public void UpdateFlashStatus(bool isOn)
        {
            if (_isFlashOn != isOn)
            {
                _isFlashOn = isOn;
                FlashStatusChanged?.Invoke(isOn);
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
        private const int TargetFps = 30;

        public event FrameReadyEventHandler FrameReady;

        private IntPtr _ffmpegDecoderContext = IntPtr.Zero;
        private H264FfmpegDecoder _h264Decoder;
        private VirtualCamera _virtualCamera;
        private readonly object _cameraLock = new object();
        private DateTime _lastFrameTime = DateTime.MinValue;
        private long _unsupportedPacketCounter;
        private int _lastFrameWidth;
        private int _lastFrameHeight;

        private const int UdpFragmentHeaderSize = 12;
        private static readonly byte[] UdpFragmentMagic = new byte[] { (byte)'V', (byte)'C', (byte)'A', (byte)'M' };
        private const byte UdpFragmentVersion = 1;
        private const int UdpReassemblyTimeoutMs = 3000;

        private readonly object _reassemblyLock = new object();
        private readonly Dictionary<uint, UdpNalAssemblyState> _udpNalAssemblies = new Dictionary<uint, UdpNalAssemblyState>();

        public VideoProcessor()
        {
            InitializeDecoder();
        }

        private void InitializeDecoder()
        {
            Console.WriteLine("[Decoder] 正在初始化 H.264 解码器...");
            try
            {
                _h264Decoder = new H264FfmpegDecoder();
                _ffmpegDecoderContext = new IntPtr(1);
                Console.WriteLine("[Decoder] 解码器初始化完成。");
            }
            catch (Exception ex)
            {
                _ffmpegDecoderContext = IntPtr.Zero;
                Console.WriteLine($"[Decoder] 初始化失败: {ex.Message}");
            }
        }

        private void EnsureVirtualCameraInitialized(int width, int height)
        {
            if (_virtualCamera == null || _lastFrameWidth != width || _lastFrameHeight != height)
            {
                lock (_cameraLock)
                {
                    if (_virtualCamera == null || _lastFrameWidth != width || _lastFrameHeight != height)
                    {
                        _virtualCamera?.Shutdown();
                        _virtualCamera = new VirtualCamera(width, height, TargetFps);
                        _lastFrameWidth = width;
                        _lastFrameHeight = height;
                    }
                }
            }
        }

        public void ProcessUdpPacket(byte[] data)
        {
            if (_ffmpegDecoderContext == IntPtr.Zero) return;
            if (data == null || data.Length == 0) return;
            if (_h264Decoder == null) return;

            try
            {
                byte[] packetToDecode = null;
                bool isFragmentPacket;
                if (TryReassembleFragment(data, out byte[] reassembledNal, out isFragmentPacket))
                {
                    packetToDecode = reassembledNal;
                }
                else if (isFragmentPacket)
                {
                    // 片段还没收齐，继续等待更多数据。
                    return;
                }
                else if (IsAnnexBPacket(data))
                {
                    packetToDecode = data;
                }
                else
                {
                    _unsupportedPacketCounter++;
                    if (_unsupportedPacketCounter % 120 == 0)
                    {
                        Console.WriteLine("[Decoder] 收到非标准 UDP 包，忽略数据。等待 H.264 包或自定义分片包。");
                    }
                    return;
                }

                if (packetToDecode == null || packetToDecode.Length == 0)
                {
                    return;
                }

                Console.WriteLine($"[Decoder] 尝试解码 packetToDecode 长度={packetToDecode.Length} start={BitConverter.ToString(packetToDecode, 0, Math.Min(8, packetToDecode.Length))}");
                if (!_h264Decoder.TryDecode(packetToDecode, out byte[] decodedFrame, out int width, out int height))
                {
                    _unsupportedPacketCounter++;
                    if (_unsupportedPacketCounter % 120 == 0)
                    {
                        Console.WriteLine("[Decoder] 当前 UDP 包未形成可解码帧，等待更多 H.264 数据...");
                    }
                    return;
                }
                Console.WriteLine($"[Decoder] 解码成功 width={width} height={height} frameBytes={decodedFrame.Length}");

                var now = DateTime.UtcNow;
                if ((now - _lastFrameTime).TotalMilliseconds < (1000.0 / TargetFps))
                {
                    return;
                }
                _lastFrameTime = now;

                FrameReady?.Invoke(decodedFrame, width, height);

                EnsureVirtualCameraInitialized(width, height);
                _virtualCamera?.PushFrame(decodedFrame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Decoder Error] 解码处理失败: {ex.Message}");
            }
        }

        private bool IsAnnexBPacket(byte[] data)
        {
            return data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1;
        }

        private bool TryReassembleFragment(byte[] data, out byte[] nalData, out bool isFragmentPacket)
        {
            nalData = null;
            isFragmentPacket = false;

            if (data.Length < UdpFragmentHeaderSize) return false;
            if (!data.Take(UdpFragmentMagic.Length).SequenceEqual(UdpFragmentMagic)) return false;
            if (data[4] != UdpFragmentVersion) return false;

            byte flags = data[5];
            int frameId = ((data[6] & 0xFF) << 8) | (data[7] & 0xFF);
            int nalIndex = ((data[8] & 0xFF) << 8) | (data[9] & 0xFF);
            int fragmentIndex = ((data[10] & 0xFF) << 8) | (data[11] & 0xFF);
            bool isStart = (flags & 0x1) != 0;
            bool isEnd = (flags & 0x2) != 0;

            Console.WriteLine($"[UDP Fragment] magic={Encoding.ASCII.GetString(data, 0, 4)} version={data[4]} frameId={frameId} nalIndex={nalIndex} fragmentIndex={fragmentIndex} start={isStart} end={isEnd} totalLen={data.Length}");

            if (data.Length == UdpFragmentHeaderSize && !isStart && !isEnd)
            {
                return false;
            }

            isFragmentPacket = true;
            byte[] payload = new byte[data.Length - UdpFragmentHeaderSize];
            Array.Copy(data, UdpFragmentHeaderSize, payload, 0, payload.Length);

            uint key = ((uint)frameId << 16) | (uint)nalIndex;
            lock (_reassemblyLock)
            {
                CleanupStaleReassemblies();

                if (!_udpNalAssemblies.TryGetValue(key, out UdpNalAssemblyState assembly))
                {
                    assembly = new UdpNalAssemblyState(frameId, nalIndex);
                    _udpNalAssemblies[key] = assembly;
                }

                assembly.AddFragment(fragmentIndex, payload, isStart, isEnd);
                if (!assembly.IsComplete())
                {
                    Console.WriteLine($"[UDP Fragment] 当前组包未完成 frameId={frameId} nalIndex={nalIndex} fragments={assembly.Fragments.Count}/? start={assembly.HasStart} end={assembly.HasEnd}");
                    return false;
                }

                nalData = assembly.BuildNal();
                Console.WriteLine($"[UDP Fragment] 组包完成 frameId={frameId} nalIndex={nalIndex} totalNalSize={nalData.Length}");
                _udpNalAssemblies.Remove(key);
                return true;
            }
        }

        private void CleanupStaleReassemblies()
        {
            if (_udpNalAssemblies.Count == 0) return;
            var threshold = DateTime.UtcNow.AddMilliseconds(-UdpReassemblyTimeoutMs);
            var staleKeys = new List<uint>();
            foreach (var pair in _udpNalAssemblies)
            {
                if (pair.Value.LastUpdated < threshold)
                {
                    staleKeys.Add(pair.Key);
                }
            }
            foreach (var key in staleKeys)
            {
                _udpNalAssemblies.Remove(key);
            }
        }

        private sealed class UdpNalAssemblyState
        {
            public int FrameId { get; }
            public int NalIndex { get; }
            public bool HasStart { get; private set; }
            public bool HasEnd { get; private set; }
            public int? ExpectedLastFragmentIndex { get; private set; }
            public SortedDictionary<int, byte[]> Fragments { get; } = new SortedDictionary<int, byte[]>();
            public DateTime LastUpdated { get; private set; }

            public UdpNalAssemblyState(int frameId, int nalIndex)
            {
                FrameId = frameId;
                NalIndex = nalIndex;
                LastUpdated = DateTime.UtcNow;
            }

            public void AddFragment(int fragmentIndex, byte[] payload, bool isStart, bool isEnd)
            {
                LastUpdated = DateTime.UtcNow;
                if (isStart) HasStart = true;
                if (isEnd)
                {
                    HasEnd = true;
                    ExpectedLastFragmentIndex = fragmentIndex;
                }
                if (!Fragments.ContainsKey(fragmentIndex))
                {
                    Fragments[fragmentIndex] = payload;
                }
            }

            public bool IsComplete()
            {
                if (!HasStart || !HasEnd || !ExpectedLastFragmentIndex.HasValue) return false;
                if (Fragments.Count != ExpectedLastFragmentIndex.Value + 1) return false;

                int expected = 0;
                foreach (var index in Fragments.Keys)
                {
                    if (index != expected) return false;
                    expected++;
                }
                return true;
            }

            public byte[] BuildNal()
            {
                int totalSize = 0;
                foreach (var payload in Fragments.Values)
                {
                    totalSize += payload.Length;
                }

                var nal = new byte[totalSize];
                int offset = 0;
                foreach (var payload in Fragments.Values)
                {
                    Array.Copy(payload, 0, nal, offset, payload.Length);
                    offset += payload.Length;
                }
                return nal;
            }
        }

        public void Shutdown()
        {
            if (_ffmpegDecoderContext != IntPtr.Zero)
            {
                Console.WriteLine("[Decoder] 正在关闭解码器并释放资源...");
                _h264Decoder?.Dispose();
                _h264Decoder = null;
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

            // 恢复使用系统随机分配的源端口
            // 因为部分路由器或手机系统会直接丢弃 "源端口 == 目标端口" 的 UDP 广播包
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

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 1. 获取所有的网络接口
                    var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var networkInterface in networkInterfaces)
                    {
                        // 过滤掉未开启的接口和环回接口
                        if (networkInterface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                            networkInterface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        {
                            continue;
                        }

                        // 找到所有的 IPv4 的 IP
                        foreach (var ipInfo in networkInterface.GetIPProperties().UnicastAddresses)
                        {
                            if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                // 计算针对该子网的定向广播地址
                                IPAddress broadcastAddress = GetBroadcastAddress(ipInfo.Address, ipInfo.IPv4Mask);
                                if (broadcastAddress != null)
                                {
                                    IPEndPoint broadcastEndpoint = new IPEndPoint(broadcastAddress, DiscoveryPort);
                                    await _udpClient.SendAsync(messageBytes, messageBytes.Length, broadcastEndpoint);
                                }
                            }
                        }
                    }

                    // 2. 同时向全局广播尝试一次，作为兜底
                    IPEndPoint globalBroadcast = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                    await _udpClient.SendAsync(messageBytes, messageBytes.Length, globalBroadcast);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Discovery Error] 广播失败: {ex.Message}");
                }

                await Task.Delay(2000, token);
            }
        }

        // 计算定向广播地址的辅助方法
        private IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            if (subnetMask == null) return null;

            byte[] ipAddressBytes = address.GetAddressBytes();
            byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

            if (ipAddressBytes.Length != subnetMaskBytes.Length) return null;

            byte[] broadcastAddressBytes = new byte[ipAddressBytes.Length];
            for (int i = 0; i < broadcastAddressBytes.Length; i++)
            {
                broadcastAddressBytes[i] = (byte)(ipAddressBytes[i] | (subnetMaskBytes[i] ^ 255));
            }
            return new IPAddress(broadcastAddressBytes);
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
        private const int UdpFragmentHeaderSize = 12;
        private static readonly byte[] UdpFragmentMagic = new byte[] { (byte)'V', (byte)'C', (byte)'A', (byte)'M' };
        private const byte UdpFragmentVersion = 1;

        private TcpListener _tcpListener;
        private UdpClient _udpClient;
        private CancellationTokenSource _cancellationTokenSource;
    //private const int UdpFragmentHeaderSize = 8;
    //        private const byte UdpFragmentVersion = 1;
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
                        var remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        _stateManager.UpdateClientIp(remoteIp);
                        _stateManager.IsClientConnected = true; // 更新连接状态
                        Console.WriteLine($"[TCP] 客户端已连接: {remoteIp}");
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
                        var data = result.Buffer;

                        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 1)
                        {
                            Console.WriteLine($"[UDP] 收到 Annex-B H.264 包 from={result.RemoteEndPoint} 长度={data.Length} 头={BitConverter.ToString(data, 0, Math.Min(16, data.Length))}");
                            _videoProcessor.ProcessUdpPacket(result.Buffer);
                        }
                        else if (data.Length >= UdpFragmentHeaderSize && data.Take(UdpFragmentMagic.Length).SequenceEqual(UdpFragmentMagic) && data[4] == UdpFragmentVersion)
                        {
                            Console.WriteLine($"[UDP] 收到 H.264 分片包 from={result.RemoteEndPoint} 长度={data.Length} 头={BitConverter.ToString(data, 0, Math.Min(16, data.Length))}");
                            _videoProcessor.ProcessUdpPacket(result.Buffer);
                        }
                        else
                        {
                            Console.WriteLine($"[UDP] 非标准H264包 from={result.RemoteEndPoint}，前16字节: {BitConverter.ToString(data, 0, Math.Min(16, data.Length))} 长度: {data.Length}");
                        }
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
