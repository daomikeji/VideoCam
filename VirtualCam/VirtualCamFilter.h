// VirtualCamFilter.h - 虚拟摄像头 DirectShow 源过滤器定义
#pragma once

// --- DirectShow 必备头文件 ---
#include <streams.h>
#include <initguid.h>
#include <combaseapi.h>

// --- 视频配置常量 (示例) ---
const int DEFAULT_WIDTH = 1280;
const int DEFAULT_HEIGHT = 720;
const REFERENCE_TIME FRAME_INTERVAL = 333333; // 10M / 30 fps

// 引入 VirtualCamDriver.cpp 中定义的导出函数
extern "C" {
    // 供过滤器调用，从 C# 缓冲区获取最新帧数据
    size_t GetLatestFrame(BYTE* pBuffer, size_t bufferSize);
}

// ---------------------------------------------------------------------
// 步骤零：定义 GUID
// 注意：以下 CLSID 必须是全新的，请勿在实际项目中使用此示例值
// 请使用 guidgen 工具生成新的 CLSID
// {71805287-5C1E-4290-B35C-1704E588ADE0}
DEFINE_GUID(CLSID_VirtualCamFilter,
    0x71805287, 0x5c1e, 0x4290, 0xb3, 0x5c, 0x17, 0x4, 0xe5, 0x88, 0xad, 0xe0); // 新 GUID 示例



// -----------------------------------------------------------
// 步骤一：数据流引脚类 (CVirtualCamStream)
// -----------------------------------------------------------
class CVirtualCamStream : public CSourceStream
{
public:
    // Provide two constructors: one matching common derived usage and one that
    // forwards an explicit object name to the base CSourceStream constructor.
    CVirtualCamStream(HRESULT* phr, CSource* pFilter, LPCWSTR pPinName);
    CVirtualCamStream(LPCTSTR pObjectName, HRESULT* phr, CSource* pFilter, LPCWSTR pPinName);
    ~CVirtualCamStream();

    // 核心方法：填充媒体缓冲区，获取帧数据
    HRESULT FillBuffer(IMediaSample* pSample);
    HRESULT GetMediaType(CMediaType* pMediaType);
    HRESULT CheckMediaType(const CMediaType* pMediaType);
    HRESULT DecideBufferSize(IMemAllocator* pAlloc, ALLOCATOR_PROPERTIES* pProp);
    HRESULT SetMediaType(const CMediaType* pMediaType);

private:
    VIDEOINFOHEADER m_vidInfo;
    long m_frameCount;
};

// -----------------------------------------------------------
// 步骤二：过滤器主类 (CVirtualCamFilter)
// -----------------------------------------------------------
class CVirtualCamFilter : public CSource
{
public:
    static CUnknown* WINAPI CreateInstance(LPUNKNOWN lpunk, HRESULT* phr);

private:
    CVirtualCamFilter(LPUNKNOWN lpunk, HRESULT* phr);
};