#include "pch.h"
#include <iostream>
#include <windows.h> 
#include <vector>    
#include <mutex>     
#include <cstring>   // For memcpy

// --- 驱动内部模拟状态 ---
namespace VirtualCam {
    // 线程安全锁
    std::mutex g_FrameMutex;

    // 结构体用于存储最新的视频帧和元数据
    struct FrameBuffer {
        std::vector<BYTE> data; // 存储原始帧数据的缓冲区
        int width = 0;
        int height = 0;
        bool isNewFrameReady = false; // 标记是否有新帧可用
    };

    FrameBuffer g_CurrentFrame; // 驱动内部共享的帧缓冲区
}

// ---------------------------------------------------------------------

extern "C" {

    /// <summary>
    /// 接收来自 C# 应用的视频帧数据（生产者）。
    /// </summary>
    /// <param name="pFrameData">指向帧数据内存块的指针。</param>
    /// <param name="width">视频帧的宽度。</param>
    /// <param name="height">视频帧的高度。</param>
    __declspec(dllexport) void PushNewFrame(BYTE* pFrameData, int width, int height)
    {
        if (pFrameData == nullptr || width <= 0 || height <= 0) return;

        // 假设使用 YUY2 格式 (每像素 2 字节)
        size_t dataSize = (size_t)width * height * 2;

        std::lock_guard<std::mutex> lock(VirtualCam::g_FrameMutex);

        try
        {
            if (VirtualCam::g_CurrentFrame.data.size() != dataSize)
            {
                VirtualCam::g_CurrentFrame.data.resize(dataSize);
            }

            // 复制数据到内部缓冲区
            memcpy(VirtualCam::g_CurrentFrame.data.data(), pFrameData, dataSize);

            VirtualCam::g_CurrentFrame.width = width;
            VirtualCam::g_CurrentFrame.height = height;
            VirtualCam::g_CurrentFrame.isNewFrameReady = true;

            // std::cout << "[Driver] C# 已推送新帧。" << std::endl;
        }
        catch (const std::exception& e)
        {
            // 错误处理
        }
    }

    /// <summary>
    /// 供 DirectShow 过滤器调用的函数（消费者）。
    /// </summary>
    /// <param name="pBuffer">调用者提供的用于接收数据的缓冲区。</param>
    /// <param name="bufferSize">调用者提供的缓冲区大小。</param>
    /// <returns>返回实际复制的字节数，如果无新帧则返回 0。</returns>
    __declspec(dllexport) size_t GetLatestFrame(BYTE* pBuffer, size_t bufferSize)
    {
        std::lock_guard<std::mutex> lock(VirtualCam::g_FrameMutex);

        if (VirtualCam::g_CurrentFrame.isNewFrameReady &&
            VirtualCam::g_CurrentFrame.data.size() <= bufferSize)
        {
            // 复制数据到 DirectShow 请求的缓冲区
            memcpy(pBuffer, VirtualCam::g_CurrentFrame.data.data(), VirtualCam::g_CurrentFrame.data.size());

            VirtualCam::g_CurrentFrame.isNewFrameReady = false;
            return VirtualCam::g_CurrentFrame.data.size();
        }

        return 0; // 无新帧或缓冲区太小
    }

} // extern "C"