using System;
using System.Windows;
using VideoCamServer.Helper;
using VideoCamServer.Services;

namespace VideoCamServer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private VideoProcessor _videoProcessor;
        private NetworkListenerService _networkListener;
        private DiscoveryService _discoveryService;
        private ServerStateManager _stateManager;

        public ServerStateManager StateManager => _stateManager;
        public VideoProcessor VideoProcessor => _videoProcessor;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化后台服务
            InitializeBackgroundServices();

            // 手动显示主窗口
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void InitializeBackgroundServices()
        {
            try
            {
                LogHelper.WriteInfoLog("[App] 正在初始化后台服务...");

                // 1. 初始化状态管理器
                _stateManager = new ServerStateManager();

                // 订阅状态更新事件（可选）
                _stateManager.StatusUpdated += status =>
                {
                    LogHelper.WriteInfoLog($"[状态更新] {status}");
                };

                // 2. 初始化视频处理器
                _videoProcessor = new VideoProcessor();

                // 3. 初始化网络监听服务
                _networkListener = new NetworkListenerService(_videoProcessor, _stateManager);
                _networkListener.StartListeners();

                // 4. 初始化服务发现
                _discoveryService = new DiscoveryService(
                    NetworkListenerService.TcpPort,
                    NetworkListenerService.UdpPort
                );
                _discoveryService.StartBroadcast();

                LogHelper.WriteInfoLog("[App] 后台服务初始化完成");
                LogHelper.WriteInfoLog($"[App] TCP 端口: {NetworkListenerService.TcpPort}");
                LogHelper.WriteInfoLog($"[App] UDP 端口: {NetworkListenerService.UdpPort}");
                LogHelper.WriteInfoLog($"[App] 发现端口: {DiscoveryService.DiscoveryPort}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"初始化后台服务失败：{ex.Message}",
                    "启动错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 清理资源
            LogHelper.WriteInfoLog("[App] 正在关闭后台服务...");

            _networkListener?.StopListeners();
            _discoveryService?.StopBroadcast();
            _videoProcessor?.Shutdown();

            LogHelper.WriteInfoLog("[App] 后台服务已关闭");

            base.OnExit(e);
        }
    }
}
