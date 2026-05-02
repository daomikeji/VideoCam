using System;
using System.Runtime.InteropServices;

namespace VideoCamServer.Services
{
    public interface IVirtualCameraSink : IDisposable
    {
        int Width { get; }
        int Height { get; }
        int Fps { get; }
        bool IsAvailable { get; }
        void PushFrame(byte[] frameData);
        void Shutdown();
    }

    /// <summary>
    /// OBS Virtual Camera 实现
    /// 与 obs-virtualsource.dll 进行交互，将视频帧推送到虚拟摄像头
    /// </summary>
    public sealed class VirtualCamera : IVirtualCameraSink
    {
        private bool _isShutdown;
        private bool _isStarted;
        private long _frameCounter;
        private readonly object _pushLock = new object();

        public int Width { get; }
        public int Height { get; }
        public int Fps { get; }
        public bool IsAvailable => !_isShutdown && _isStarted;

        public VirtualCamera(int width, int height, int fps)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

            Width = width;
            Height = height;
            Fps = fps;

            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // 启动 OBS 虚拟摄像头
                bool success = OBSVirtualCameraInterop.obs_virtual_cam_start();
                if (success)
                {
                    _isStarted = true;
                    Console.WriteLine($"[VirtualCamera] OBS 虚拟摄像头已启动 ({Width}x{Height} @ {Fps}fps)");
                }
                else
                {
                    Console.WriteLine("[VirtualCamera] 警告: OBS 虚拟摄像头启动失败");
                    _isStarted = false;
                }
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("[VirtualCamera] 错误: 找不到 obs-virtualsource.dll，请确保已正确安装 OBS Virtual Camera");
                _isStarted = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VirtualCamera] 错误: 初始化失败 - {ex.Message}");
                _isStarted = false;
            }
        }

        public void PushFrame(byte[] frameData)
        {
            if (_isShutdown)
            {
                throw new ObjectDisposedException(nameof(VirtualCamera));
            }

            if (frameData == null)
            {
                throw new ArgumentNullException(nameof(frameData));
            }

            if (!_isStarted)
            {
                // 虚拟摄像头未启动，跳过帧推送
                return;
            }

            lock (_pushLock)
            {
                try
                {
                    // 将帧数据固定在内存中
                    GCHandle pinnedArray = GCHandle.Alloc(frameData, GCHandleType.Pinned);
                    try
                    {
                        IntPtr dataPtr = pinnedArray.AddrOfPinnedObject();

                        // 计算时间戳（纳秒）
                        ulong timestamp = (ulong)(_frameCounter * 1000000000L / Fps);
                        _frameCounter++;

                        // 计算行大小（stride）
                        // 假设是 BGRA 格式（4 字节/像素）
                        uint linesize = (uint)(Width * 4);

                        // 创建视频帧结构
                        var frame = new OBSVirtualCameraInterop.VideoFrame
                        {
                            data = dataPtr,
                            width = (uint)Width,
                            height = (uint)Height,
                            linesize = linesize,
                            timestamp = timestamp,
                            format = OBSVirtualCameraInterop.VideoFormat.VIDEO_FORMAT_BGRA
                        };

                        // 推送帧到 OBS 虚拟摄像头
                        OBSVirtualCameraInterop.obs_virtual_cam_video(ref frame);
                    }
                    finally
                    {
                        pinnedArray.Free();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VirtualCamera] 错误: 推送帧失败 - {ex.Message}");
                }
            }
        }

        public void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            try
            {
                if (_isStarted)
                {
                    OBSVirtualCameraInterop.obs_virtual_cam_stop();
                    Console.WriteLine("[VirtualCamera] OBS 虚拟摄像头已停止");
                    _isStarted = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VirtualCamera] 警告: 关闭虚拟摄像头时出错 - {ex.Message}");
            }
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}