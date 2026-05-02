using System;
using System.Runtime.InteropServices;

namespace VideoCamServer.Services
{
    /// <summary>
    /// OBS Virtual Camera 互操作层
    /// 用于与 obs-virtualsource.dll 通信
    /// </summary>
    public static class OBSVirtualCameraInterop
    {
        private const string DllName = "obs-virtualsource.dll";

        /// <summary>
        /// 视频格式枚举
        /// </summary>
        public enum VideoFormat : uint
        {
            VIDEO_FORMAT_NONE = 0,
            VIDEO_FORMAT_I420,      // planar 420 format
            VIDEO_FORMAT_NV12,      // two-plane 420 format
            VIDEO_FORMAT_YVYU,      // packed 422 format
            VIDEO_FORMAT_YUY2,      // packed 422 format
            VIDEO_FORMAT_UYVY,      // packed 422 format
            VIDEO_FORMAT_RGBA,      // packed RGBA 8:8:8:8
            VIDEO_FORMAT_BGRA,      // packed BGRA 8:8:8:8
            VIDEO_FORMAT_BGRX,      // packed BGRX 8:8:8:8
            VIDEO_FORMAT_Y800,      // grayscale
            VIDEO_FORMAT_I444       // planar 444 format
        }

        /// <summary>
        /// 视频帧数据结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VideoFrame
        {
            public IntPtr data;          // 指向帧数据的指针
            public uint width;           // 视频宽度
            public uint height;          // 视频高度
            public uint linesize;        // 每行的字节数（stride）
            public ulong timestamp;      // 时间戳（纳秒）
            public VideoFormat format;   // 视频格式
        }

        /// <summary>
        /// 初始化虚拟摄像头
        /// </summary>
        /// <returns>如果成功返回 true</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool obs_virtual_cam_start();

        /// <summary>
        /// 停止虚拟摄像头
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void obs_virtual_cam_stop();

        /// <summary>
        /// 发送视频帧到虚拟摄像头
        /// </summary>
        /// <param name="frame">视频帧结构</param>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void obs_virtual_cam_video(ref VideoFrame frame);

        /// <summary>
        /// 检查虚拟摄像头是否正在运行
        /// </summary>
        /// <returns>如果正在运行返回 true</returns>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool obs_virtual_cam_active();
    }
}
