using System;
using System.Runtime.InteropServices;

namespace VideoCamServer.Services
{
    /// <summary>
    /// VirtualCamera 类用于将视频帧推送到虚拟摄像头。
    /// </summary>
    public class VirtualCamera
    {
        private const string DllName = "obs-virtual-cam.dll"; // 假设 obs-virtual-cam 提供了一个 DLL

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateVirtualCamera(int width, int height, int fps);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void PushFrame(IntPtr camera, IntPtr frameData);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DestroyVirtualCamera(IntPtr camera);

        private IntPtr _cameraHandle;

        public VirtualCamera(int width, int height, int fps)
        {
            _cameraHandle = CreateVirtualCamera(width, height, fps);
            if (_cameraHandle == IntPtr.Zero)
            {
                throw new Exception("Failed to initialize virtual camera.");
            }
        }

        public void PushFrame(byte[] frameData)
        {
            if (_cameraHandle == IntPtr.Zero) return;

            GCHandle pinnedArray = GCHandle.Alloc(frameData, GCHandleType.Pinned);
            IntPtr pointer = pinnedArray.AddrOfPinnedObject();

            try
            {
                PushFrame(_cameraHandle, pointer);
            }
            finally
            {
                pinnedArray.Free();
            }
        }

        public void Shutdown()
        {
            if (_cameraHandle != IntPtr.Zero)
            {
                DestroyVirtualCamera(_cameraHandle);
                _cameraHandle = IntPtr.Zero;
            }
        }
    }
}