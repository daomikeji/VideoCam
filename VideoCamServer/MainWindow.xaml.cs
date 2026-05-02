using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoCamServer.Services;

namespace VideoCamServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int PreviewWidth = 640;
        private const int PreviewHeight = 360;
        private const int BytesPerPixel = 4;

        private readonly byte[] _previewBuffer = new byte[PreviewWidth * PreviewHeight * BytesPerPixel];
        private readonly WriteableBitmap _previewBitmap;
        private int _frameCounter;

        private VideoServer _videoServer;

        public MainWindow()
        {
            InitializeComponent();

            _previewBitmap = new WriteableBitmap(
                PreviewWidth,
                PreviewHeight,
                96,
                96,
                PixelFormats.Bgr32,
                null);

            VideoPreviewImage.Source = _previewBitmap;
            RenderPlaceholderFrame("等待视频流...");
            InitializeServerAndEvents();
        }

        private void InitializeServerAndEvents()
        {
            // 初始化网络服务
            _videoServer = new VideoServer();

            // 订阅网络服务事件：这是将后台网络线程结果推送到 UI 线程的关键
            _videoServer.OnServerStatusChanged += OnServerStatusChanged;
            _videoServer.OnClientConnected += OnClientConnected;
            _videoServer.OnClientDisconnected += OnClientDisconnected;
            _videoServer.OnCommandReceived += OnCommandReceived;
            _videoServer.OnVideoDataReceived += OnVideoDataReceived;

            // 初始UI状态设置
            ServerStatusLabel.Text = "已停止";
            ServerStatusLabel.Foreground = Brushes.Red;
            ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // Blue
        }

        // ------------------ UI 事件处理 ------------------

        private async void ToggleServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_videoServer.IsRunning)
            {
                await _videoServer.Stop();
                RenderPlaceholderFrame("服务器已停止");
            }
            else
            {
                // 启动 TCP 9000 (控制) 和 UDP 9001 (视频)
                await _videoServer.Start(9000, 9001);
            }
        }

        // ------------------ 网络事件处理器 (确保在 UI 线程上运行) ------------------

        private void OnServerStatusChanged(bool isRunning)
        {
            // 使用 Dispatcher.Invoke 确保 UI 更新操作在主线程上执行
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    ServerStatusLabel.Text = "正在监听...";
                    ServerStatusLabel.Foreground = Brushes.Orange;
                    ToggleServerButton.Content = "关闭服务器";
                    ToggleServerButton.Background = Brushes.DarkRed;
                    VideoStatusText.Text = "等待客户端连接...";
                }
                else
                {
                    ServerStatusLabel.Text = "已停止";
                    ServerStatusLabel.Foreground = Brushes.Red;
                    ToggleServerButton.Content = "启动服务器";
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204));
                    VideoStatusText.Text = "等待服务器启动...";
                }
            });
        }

        private void OnClientConnected(string ipAddress)
        {
            Dispatcher.Invoke(() =>
            {
                ClientIPLabel.Text = ipAddress;
                ServerStatusLabel.Text = "客户端已连接";
                ServerStatusLabel.Foreground = Brushes.Green;
                VideoStatusText.Text = "正在接收视频流...";
            });
        }

        private void OnClientDisconnected()
        {
            Dispatcher.Invoke(() =>
            {
                ClientIPLabel.Text = "无连接";
                ServerStatusLabel.Text = "正在监听...";
                ServerStatusLabel.Foreground = Brushes.Orange;
                VideoStatusText.Text = "客户端断开，等待新连接...";
                RenderPlaceholderFrame("等待新的视频流...");
            });
        }

        // 接收到移动端控制命令 (TCP)
        private void OnCommandReceived(string command)
        {
            // 命令示例: SWITCH_CAMERA:FRONT, TOGGLE_FLASH:ON
            string[] parts = command.Split(':');
            string type = parts[0];
            string value = parts.Length > 1 ? parts[1] : "";

            Dispatcher.Invoke(() =>
            {
                if (type == "SWITCH_CAMERA")
                {
                    CameraStateLabel.Text = value == "FRONT" ? "前置 (Front)" : "后置 (Back)";
                }
                else if (type == "TOGGLE_FLASH")
                {
                    FlashStateLabel.Text = value == "ON" ? "开启 (ON)" : "关闭 (OFF)";
                    FlashStateLabel.Foreground = value == "ON" ? Brushes.Gold : Brushes.Black;
                }
                // 在这里可以添加其他命令（如分辨率、帧率控制等）
            });
        }

        // 接收到视频数据包 (UDP)
        private void OnVideoDataReceived(byte[] data)
        {
            Dispatcher.Invoke(() => RenderPreviewFrame(data));
        }

        private void RenderPreviewFrame(byte[] data)
        {
            _frameCounter++;
            int seed = data.Length > 0 ? data[0] : _frameCounter;

            for (int y = 0; y < PreviewHeight; y++)
            {
                for (int x = 0; x < PreviewWidth; x++)
                {
                    int pixelIndex = (y * PreviewWidth + x) * BytesPerPixel;
                    byte blue = (byte)((x + seed) % 256);
                    byte green = (byte)((y + seed * 2) % 256);
                    byte red = (byte)((x + y + seed * 3 + _frameCounter) % 256);

                    _previewBuffer[pixelIndex] = blue;
                    _previewBuffer[pixelIndex + 1] = green;
                    _previewBuffer[pixelIndex + 2] = red;
                    _previewBuffer[pixelIndex + 3] = 0;
                }
            }

            DrawCenterMarker();
            _previewBitmap.WritePixels(new Int32Rect(0, 0, PreviewWidth, PreviewHeight), _previewBuffer, PreviewWidth * BytesPerPixel, 0);
        }

        private void RenderPlaceholderFrame(string message)
        {
            Array.Clear(_previewBuffer, 0, _previewBuffer.Length);

            for (int y = 0; y < PreviewHeight; y++)
            {
                for (int x = 0; x < PreviewWidth; x++)
                {
                    int pixelIndex = (y * PreviewWidth + x) * BytesPerPixel;
                    byte shade = (byte)(20 + (y * 40 / PreviewHeight));
                    _previewBuffer[pixelIndex] = shade;
                    _previewBuffer[pixelIndex + 1] = shade;
                    _previewBuffer[pixelIndex + 2] = shade;
                    _previewBuffer[pixelIndex + 3] = 0;
                }
            }

            DrawCenterMarker();
            _previewBitmap.WritePixels(new Int32Rect(0, 0, PreviewWidth, PreviewHeight), _previewBuffer, PreviewWidth * BytesPerPixel, 0);
            VideoStatusText.Text = message;
        }

        private void DrawCenterMarker()
        {
            int centerX = PreviewWidth / 2;
            int centerY = PreviewHeight / 2;

            for (int offset = -40; offset <= 40; offset++)
            {
                SetPixel(centerX + offset, centerY, 255, 255, 255);
                SetPixel(centerX, centerY + offset, 255, 255, 255);
            }
        }

        private void SetPixel(int x, int y, byte red, byte green, byte blue)
        {
            if (x < 0 || x >= PreviewWidth || y < 0 || y >= PreviewHeight)
            {
                return;
            }

            int pixelIndex = (y * PreviewWidth + x) * BytesPerPixel;
            _previewBuffer[pixelIndex] = blue;
            _previewBuffer[pixelIndex + 1] = green;
            _previewBuffer[pixelIndex + 2] = red;
            _previewBuffer[pixelIndex + 3] = 0;
        }
    }
}
