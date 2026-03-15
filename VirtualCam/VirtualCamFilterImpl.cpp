#include "pch.h"
#include "VirtualCamFilter.h"
#include <winbase.h> // DllMain
#include <atlbase.h> // CComPtr (可选，用于 COM 资源管理)
#include <iostream>

// 声明 BaseClasses 提供的入口点函数（3 参数版本）
extern "C" BOOL WINAPI DllEntryPoint(HINSTANCE, ULONG, LPVOID);

// --- 过滤器的名称定义（显示在 Zoom/OBS 列表中）---
// 请在这里修改为您自定义的名称
#define FILTER_NAME L"Canvas Virtual Cam"

// -----------------------------------------------------------
// I. 过滤器工厂模板和注册信息
// -----------------------------------------------------------

// 过滤器描述符 (名称、CLSID、创建函数)
const AMOVIESETUP_FILTER amvst_VirtualCamFilter =
{
    &CLSID_VirtualCamFilter,    // Filter CLSID
    FILTER_NAME,                // Filter name
    MERIT_DO_NOT_USE,           // Merit（应设置为高优先级，但在测试中保持默认）
    0,                          // Number of pins
    NULL                        // Pin information
};

// 过滤器工厂列表
CFactoryTemplate g_Templates[] =
{
    {
        FILTER_NAME,
        &CLSID_VirtualCamFilter,
        CVirtualCamFilter::CreateInstance,
        NULL,
        &amvst_VirtualCamFilter
    }
};

int g_cTemplates = sizeof(g_Templates) / sizeof(g_Templates[0]);

// -----------------------------------------------------------
// II. 过滤器主类实现 (CVirtualCamFilter)
// -----------------------------------------------------------

// 静态创建函数：由 COM 系统调用来创建过滤器实例
CUnknown* WINAPI CVirtualCamFilter::CreateInstance(LPUNKNOWN lpunk, HRESULT* phr)
{
    CVirtualCamFilter* pFilter = new CVirtualCamFilter(lpunk, phr);
    if (pFilter == NULL)
    {
        *phr = E_OUTOFMEMORY;
    }
    return pFilter;
}

// 构造函数：创建并连接数据流引脚
CVirtualCamFilter::CVirtualCamFilter(LPUNKNOWN lpunk, HRESULT* phr)
    : CSource(FILTER_NAME, lpunk, CLSID_VirtualCamFilter)
{
    // 创建数据流引脚实例，并将其添加到过滤器中
    CVirtualCamStream* pStream = new CVirtualCamStream(phr, this, L"Output");
    if (pStream == NULL)
    {
        *phr = E_OUTOFMEMORY;
        // 如果创建引脚失败，CSource 的基类构造函数将自动清理
    }
}

// -----------------------------------------------------------
// III. 数据流引脚类实现 (CVirtualCamStream)
// -----------------------------------------------------------

CVirtualCamStream::CVirtualCamStream(LPCTSTR pObjectName, HRESULT* phr, CSource* pFilter, LPCWSTR pPinName)
    : CSourceStream(pObjectName, phr, pFilter, pPinName), m_frameCount(0)
{
}

// 原始构造函数，直接调用上面的重载
CVirtualCamStream::CVirtualCamStream(HRESULT* phr, CSource* pFilter, LPCWSTR pPinName)
    : CVirtualCamStream(L"VirtualCamStream", phr, pFilter, pPinName)
{
}

CVirtualCamStream::~CVirtualCamStream()
{
    // 析构函数，无需特殊清理
}

// 决定缓冲区大小：DirectShow 调用此方法来配置内存分配器
HRESULT CVirtualCamStream::DecideBufferSize(IMemAllocator* pAlloc, ALLOCATOR_PROPERTIES* pProp)
{
    HRESULT hr = NOERROR;

    // YUY2 格式，每像素 2 字节
    const long bufferSize = DEFAULT_WIDTH * DEFAULT_HEIGHT * 2;

    // 要求分配器分配我们所需大小的缓冲区
    pProp->cBuffers = 2;              // 至少需要两个缓冲区
    pProp->cbBuffer = bufferSize;     // 帧数据大小

    ALLOCATOR_PROPERTIES Actual;
    hr = pAlloc->SetProperties(pProp, &Actual);

    if (FAILED(hr)) return hr;
    if (Actual.cbBuffer < pProp->cbBuffer) return E_FAIL;

    return S_OK;
}

// 关键方法：填充媒体缓冲区（从 C# 驱动获取帧数据）
HRESULT CVirtualCamStream::FillBuffer(IMediaSample* pSample)
{
    // 1. 获取 DirectShow 提供的空缓冲区
    BYTE* pData;
    HRESULT hr = pSample->GetPointer(&pData);
    if (FAILED(hr)) return hr;

    // 2. 获取缓冲区最大容量
    long maxDataLength = pSample->GetSize();

    // 3. 调用 VirtualCamDriver.cpp 中的函数来获取最新帧
    // 这是连接 C# 应用和 DirectShow 的关键步骤
    size_t actualDataSize = GetLatestFrame(pData, maxDataLength);

    if (actualDataSize > 0)
    {
        // 4. 设置媒体样本的实际数据长度
        pSample->SetActualDataLength((long)actualDataSize);

        // 5. 设置时间戳 (关键步骤：决定帧率)
        REFERENCE_TIME startTime = m_frameCount * FRAME_INTERVAL;
        REFERENCE_TIME endTime = startTime + FRAME_INTERVAL;
        pSample->SetTime(&startTime, &endTime);
        pSample->SetSyncPoint(TRUE); // 标记这是一个同步点

        m_frameCount++;
        return S_OK; // 成功
    }
    else
    {
        // 如果 GetLatestFrame 返回 0，说明没有新帧可用，暂停等待
        Sleep(10); // 暂停 10 毫秒，防止自旋（忙等待）
        pSample->SetActualDataLength(0);
        return S_OK; // 仍然返回 S_OK，告诉基础类继续尝试
    }
}

// 设置媒体类型：定义您的过滤器支持的视频格式
HRESULT CVirtualCamStream::GetMediaType(CMediaType* pMediaType)
{
    // 检查是否已经设置了类型
    if (!pMediaType) return E_POINTER;

    // 设置主类型和子类型
    pMediaType->SetType(&MEDIATYPE_Video);
    pMediaType->SetSubtype(&MEDIASUBTYPE_YUY2); // 常用格式：YUY2
    pMediaType->SetFormatType(&FORMAT_VideoInfo);

    // 设置 VIDEOINFOHEADER 结构体
    VIDEOINFOHEADER* pVideoInfo = (VIDEOINFOHEADER*)pMediaType->AllocFormatBuffer(sizeof(VIDEOINFOHEADER));
    if (pVideoInfo == NULL) return E_OUTOFMEMORY;

    ZeroMemory(pVideoInfo, sizeof(VIDEOINFOHEADER));

    pVideoInfo->bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    pVideoInfo->bmiHeader.biWidth = DEFAULT_WIDTH;
    pVideoInfo->bmiHeader.biHeight = DEFAULT_HEIGHT;
    pVideoInfo->bmiHeader.biPlanes = 1;
    pVideoInfo->bmiHeader.biBitCount = 16; // YUY2 是 16 位格式
    pVideoInfo->bmiHeader.biCompression = MAKEFOURCC('Y', 'U', 'Y', '2');
    pVideoInfo->bmiHeader.biSizeImage = DEFAULT_WIDTH * DEFAULT_HEIGHT * 2; // 1280 * 720 * 2 = 1843200

    pVideoInfo->AvgTimePerFrame = FRAME_INTERVAL; // 帧间隔

    pMediaType->SetFormat((BYTE*)pVideoInfo, sizeof(VIDEOINFOHEADER));

    return S_OK;
}

// 检查媒体类型：过滤器只支持一种媒体类型
HRESULT CVirtualCamStream::CheckMediaType(const CMediaType* pMediaType)
{
    if (*pMediaType->Type() != MEDIATYPE_Video) return E_INVALIDARG;
    if (*pMediaType->Subtype() != MEDIASUBTYPE_YUY2) return E_INVALIDARG;

    // 可以在这里添加更多检查，例如分辨率是否匹配

    return S_OK;
}

HRESULT CVirtualCamStream::SetMediaType(const CMediaType* pMediaType)
{
    // 确认类型合法
    HRESULT hr = CheckMediaType(pMediaType);
    if (FAILED(hr)) return hr;

    // 基类处理媒体类型设置
    return CSourceStream::SetMediaType(pMediaType);
}

// -----------------------------------------------------------
// IV. DLL 入口点和 COM 注册/注销
// -----------------------------------------------------------

// 必须包含 DllMain，用于初始化 DirectShow 基础类
extern "C" BOOL WINAPI DllMain(HINSTANCE hInst, ULONG ulReason, LPVOID pvReserved)
{
    // 调用 BaseClasses 提供的标准入口点（3 个参数版本）
    return DllEntryPoint(hInst, ulReason, pvReserved);
}

// 导出函数：注册服务器
// 当用户运行 'regsvr32 YourFilter.dll' 时调用
STDAPI DllRegisterServer()
{
    // 使用 DirectShow 基础类提供的注册函数
    return AMovieDllRegisterServer2(TRUE);
}

// 导出函数：注销服务器
// 当用户运行 'regsvr32 /u YourFilter.dll' 时调用
STDAPI DllUnregisterServer()
{
    // 使用 DirectShow 基础类提供的注销函数
    return AMovieDllRegisterServer2(FALSE);
}