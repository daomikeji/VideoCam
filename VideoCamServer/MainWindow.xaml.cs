using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        private ServerStateManager _stateManager;

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
            var app = Application.Current as App;
            _stateManager = app?.StateManager;
            var videoProcessor = app?.VideoProcessor;

            if (_stateManager != null)
            {
                _stateManager.ClientConnectionChanged += OnClientConnectionChanged;
                _stateManager.ClientIpChanged += OnClientIpChanged;
                _stateManager.CameraModeChanged += OnCameraModeChanged;
                _stateManager.FlashStatusChanged += OnFlashStatusChanged;
                _stateManager.StatusUpdated += OnStatusUpdated;
            }

            if (videoProcessor != null)
            {
                videoProcessor.FrameReady += OnFrameReady;
            }

            ServerStatusLabel.Text = "正在监听...";
            ServerStatusLabel.Foreground = Brushes.Orange;
            ToggleServerButton.Content = "服务运行中";
            ToggleServerButton.IsEnabled = false;
            ToggleServerButton.Background = Brushes.Gray;
            VideoStatusText.Text = "等待客户端连接...";
        }

        // ------------------ UI 事件处理 ------------------

        private async void ToggleServerButton_Click(object sender, RoutedEventArgs e)
        {
            await System.Threading.Tasks.Task.CompletedTask;
        }

        // ------------------ 网络事件处理器 (确保在 UI 线程上运行) ------------------

        private void OnClientConnectionChanged(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    ServerStatusLabel.Text = "客户端已连接";
                    ServerStatusLabel.Foreground = Brushes.Green;
                    VideoStatusText.Text = "正在接收视频流...";
                }
                else
                {
                    ServerStatusLabel.Text = "正在监听...";
                    ServerStatusLabel.Foreground = Brushes.Orange;
                    VideoStatusText.Text = "客户端断开，等待新连接...";
                    RenderPlaceholderFrame("等待新的视频流...");
                }
            });
        }

        private void OnClientIpChanged(string ipAddress)
        {
            Dispatcher.Invoke(() =>
            {
                ClientIPLabel.Text = string.IsNullOrWhiteSpace(ipAddress) ? "无连接" : ipAddress;
            });
        }

        private void OnCameraModeChanged(string mode)
        {
            Dispatcher.Invoke(() =>
            {
                CameraStateLabel.Text = mode == "FRONT" ? "前置 (Front)" : "后置 (Back)";
            });
        }

        private void OnFlashStatusChanged(bool isOn)
        {
            Dispatcher.Invoke(() =>
            {
                FlashStateLabel.Text = isOn ? "开启 (ON)" : "关闭 (OFF)";
                FlashStateLabel.Foreground = isOn ? Brushes.Gold : Brushes.Black;
            });
        }

        private void OnStatusUpdated(string status)
        {
            Dispatcher.Invoke(() =>
            {
                VideoStatusText.Text = status;
            });
        }

        private void OnFrameReady(byte[] frameData, int width, int height)
        {
            Dispatcher.Invoke(() => RenderPreviewFrame(frameData, width, height));
        }

        private void RenderPreviewFrame(byte[] data, int sourceWidth, int sourceHeight)
        {
            if (data == null || sourceWidth <= 0 || sourceHeight <= 0)
            {
                return;
            }

            int sourceStride = sourceWidth * BytesPerPixel;
            int expectedLength = sourceStride * sourceHeight;
            if (data.Length < expectedLength)
            {
                return;
            }

            for (int y = 0; y < PreviewHeight; y++)
            {
                int srcY = y * sourceHeight / PreviewHeight;
                for (int x = 0; x < PreviewWidth; x++)
                {
                    int srcX = x * sourceWidth / PreviewWidth;
                    int srcPixel = srcY * sourceStride + srcX * BytesPerPixel;
                    int dstPixel = (y * PreviewWidth + x) * BytesPerPixel;

                    _previewBuffer[dstPixel] = data[srcPixel];
                    _previewBuffer[dstPixel + 1] = data[srcPixel + 1];
                    _previewBuffer[dstPixel + 2] = data[srcPixel + 2];
                    _previewBuffer[dstPixel + 3] = 0;
                }
            }

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
