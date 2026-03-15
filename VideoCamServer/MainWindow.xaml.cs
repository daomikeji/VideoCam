using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VideoCamServer.Services;

namespace VideoCamServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
   
       // 请确保 VideoServer, VideoDecoder 和 VirtualCamera 类存在或被注释掉，以保证编译通过。
    //using VideoDecoder = object; // 占位符
    //using VirtualCamera = object; // 占位符

    public partial class MainWindow : Window
    {
        // 核心组件实例化
        private VideoServer _videoServer;
        //private VideoDecoder _videoDecoder = new object();
        //private VirtualCamera _virtualCamera = new object();

        public MainWindow()
        {
            InitializeComponent();
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
                }
                else
                {
                    ServerStatusLabel.Text = "已停止";
                    ServerStatusLabel.Foreground = Brushes.Red;
                    ToggleServerButton.Content = "启动服务器";
                    ToggleServerButton.Background = new SolidColorBrush(Color.FromRgb(0, 122, 204));
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
            // 视频处理在非 UI 线程中执行以保证性能
            // 1. 将数据包送给解码器
            // VideoFrame frame = _videoDecoder.Decode(data);

            // if (frame != null)
            // {
            //     // 2. 将解码后的帧推送到虚拟摄像头驱动
            //     // _virtualCamera.PushFrame(frame);
            // }
        }
    }
}
