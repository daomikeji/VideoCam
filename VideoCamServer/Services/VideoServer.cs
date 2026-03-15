using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoCamServer.Services
{
    // 此类负责处理所有网络连接和数据接收
    public class VideoServer
    {
        // 状态属性
        public bool IsRunning { get; private set; }
        public string ClientIP { get; private set; }

        // CancellationTokenSource 用于安全地停止后台任务
        private CancellationTokenSource _cancellationTokenSource;

        // TCP 监听器和 UDP 接收器
        private TcpListener _tcpListener;
        private UdpClient _udpReceiver;

        // ------------------ 事件定义 ------------------
        public event Action<bool> OnServerStatusChanged;
        public event Action<string> OnClientConnected;
        public event Action OnClientDisconnected;
        public event Action<string> OnCommandReceived;
        public event Action<byte[]> OnVideoDataReceived;

        // ------------------ 启动和停止 ------------------

        public async Task Start(int tcpPort, int udpPort)
        {
            if (IsRunning) return;

            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;

            try
            {
                // 1. 启动 TCP 监听
                _tcpListener = new TcpListener(System.Net.IPAddress.Any, tcpPort);
                _tcpListener.Start();

                // 2. 启动 UDP 接收器
                _udpReceiver = new UdpClient(udpPort);

                IsRunning = true;
                OnServerStatusChanged?.Invoke(true);

                // 3. 启动后台任务
                Task.Run(() => ListenForTCPConnections(token), token);
                Task.Run(() => ReceiveUDPVideo(token), token);

                // 4. (可选) 启动 UDP 广播发现监听

            }
            catch (Exception ex)
            {
                // 记录错误
                Stop();
            }
        }

        public Task Stop()
        {
            if (!IsRunning) return Task.CompletedTask;

            // 1. 取消所有后台任务
            _cancellationTokenSource?.Cancel();

            // 2. 关闭网络监听器
            _tcpListener?.Stop();
            _udpReceiver?.Close();

            IsRunning = false;
            ClientIP = null;
            OnClientDisconnected?.Invoke();
            OnServerStatusChanged?.Invoke(false);

            return Task.CompletedTask;
        }

        // ------------------ 后台网络任务 ------------------

        // TCP：等待客户端连接并监听控制命令
        private async Task ListenForTCPConnections(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 阻塞等待客户端连接
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();

                    // 获取客户端 IP
                    ClientIP = ((System.Net.IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    OnClientConnected?.Invoke(ClientIP);

                    // 处理控制命令
                    await HandleTCPClient(client, token);

                    // 客户端断开连接
                    OnClientDisconnected?.Invoke();
                    ClientIP = null;
                }
                catch (OperationCanceledException)
                {
                    break; // 服务器已停止
                }
                catch (Exception)
                {
                    // 错误处理
                    OnClientDisconnected?.Invoke();
                    ClientIP = null;
                }
            }
        }

        // TCP：处理单个客户端发送的命令流
        private async Task HandleTCPClient(TcpClient client, CancellationToken token)
        {
            using (var stream = client.GetStream())
            {
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // 尝试从流中读取数据
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);

                        if (bytesRead == 0) // 客户端断开连接
                            break;

                        string command = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                        OnCommandReceived?.Invoke(command);
                    }
                    catch (Exception)
                    {
                        break; // 读取失败或连接中断
                    }
                }
            }
        }


        // UDP：接收视频数据
        private async Task ReceiveUDPVideo(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 接收 UDP 数据包（阻塞操作）
                    var result = await _udpReceiver.ReceiveAsync();

                    // 只有在客户端连接时才处理数据
                    if (ClientIP != null)
                    {
                        // 触发事件，将数据发送给解码器
                        OnVideoDataReceived?.Invoke(result.Buffer);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break; // UdpClient 已关闭
                }
                catch (Exception)
                {
                    // 错误处理
                }
            }
        }
    }
}
