using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using VideoCamServer.Helper;

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
        private  WriteableBitmap _previewBitmap;
        private int _frameCounter;
        private int _isRendering = 0; // 0 = idle, 1 = rendering

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
                     StartRenderLoop();
                }
                else
                {
                    ServerStatusLabel.Text = "正在监听...";
                    ServerStatusLabel.Foreground = Brushes.Orange;
                    VideoStatusText.Text = "客户端断开，等待新连接...";
                    RenderPlaceholderFrame("等待新的视频流...");
                    EndRenderLoop();
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
        private byte[] _latestFrame;

        private readonly object _frameLock = new();

        private int _videoWidth;
        private int _videoHeight;
        //UdpLatestBuffer buffer=null;
        ZeroCopyFrame zeroCopyFrame = null;
        private bool _renderStarted;
        private void StartRenderLoop()
        {
            //if (_renderStarted)
            //    return;

            _renderStarted = true;

            CompositionTarget.Rendering += OnRendering;
        }
        private DateTime _lastRender = DateTime.MinValue;
        private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(33); // ~30fps

        private void OnRendering(object sender, EventArgs e)
        {
            byte[] frame = null;
            int width;
            int height;
            var now = DateTime.UtcNow;
            if (now - _lastRender < MinInterval)
                return;
            _lastRender = now;
            lock (_frameLock)
            {
                if (zeroCopyFrame == null)
                {
                    return;
                }
                var (buffer, length) = zeroCopyFrame.GetRenderData();
                if (buffer == null || length == 0)
                {
                    return;
                }
                //frame = new byte[length];
                //Buffer.BlockCopy(buffer, 0, frame, 0, length);
                //if (buffer == null)
                //{
                //    return;
                //}
                //if (buffer.TryReadLatest(out byte[] latest))
                //{
                //    frame = latest;
                //}
                //if (frame == null)
                //    return;
                //if (_latestFrame == null)
                //    return;

                width = _videoWidth;
                height = _videoHeight;

                //frame = (byte[])_latestFrame.Clone();
                if (_previewBitmap == null ||
                _previewBitmap.PixelWidth != width ||
                _previewBitmap.PixelHeight != height)
                {
                    VideoPreviewImage.Source = null;
                    _previewBitmap = new WriteableBitmap(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null);

                    VideoPreviewImage.Source = _previewBitmap;
                }
                // _previewBitmap.WritePixels(
                //new Int32Rect(0, 0, width, height),
                //buffer,
                //width * 4,
                //0);
                _previewBitmap.Lock();
                // 先清空为黑色
                unsafe
                {
                    var ptr = (byte*)_previewBitmap.BackBuffer;
                    var stride = _previewBitmap.BackBufferStride;
                    var previewBitmapHeight = _previewBitmap.PixelHeight;
                    for (int y = 0; y < previewBitmapHeight; y++)
                        for (int x = 0; x < stride; x++)
                            ptr[y * stride + x] = 0;
                }

                Marshal.Copy(buffer, 0, _previewBitmap.BackBuffer, buffer.Length);
                _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                _previewBitmap.Unlock();
                zeroCopyFrame.ReadEnd();

            }




        }

        private void EndRenderLoop()
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderStarted = false;
            
        }
        private void OnFrameReady(byte[] frameData, int width, int height)
        {
            if (frameData == null)
                return;

            if (width <= 0 || height <= 0)
                return;

            int expected = width * height * 4;

            if (frameData.Length < expected)
                return;

            lock (_frameLock)
            {
                _videoWidth = width;
                _videoHeight = height;
                if (zeroCopyFrame == null || zeroCopyFrame.GetRenderData().Buffer.Length != expected)
                {
                    zeroCopyFrame = new ZeroCopyFrame(expected);
                }
                //if (buffer == null)
                //{
                //    buffer = new UdpLatestBuffer(5, expected);
                //}
                if (_latestFrame == null || _latestFrame.Length != expected)
                {
                    _latestFrame = new byte[expected];
                }

                Buffer.BlockCopy(
                    frameData,
                    0,
                    _latestFrame,
                    0,
                    expected);
                //buffer.Write(_latestFrame);
                zeroCopyFrame.Push(_latestFrame);
            }

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
