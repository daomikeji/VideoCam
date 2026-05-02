# 服务端 H.264 解码器集成指南

## ?? 目标

将手机端发送的 H.264 视频流解码为 BGRA 格式的帧，然后推送到 OBS Virtual Camera。

## ?? 解码器选择

### 选项对比

| 解码器 | 优点 | 缺点 | 推荐度 |
|--------|------|------|--------|
| **FFmpeg.AutoGen** | ? 功能强大<br>? 跨平台<br>? 支持所有格式 | ?? 需要 native DLL<br>?? API 复杂 | ????? |
| **Windows Media Foundation** | ? Windows 原生<br>? 硬件加速<br>? 无需额外 DLL | ? 仅 Windows<br>?? API 复杂 | ???? |
| **OpenH264** | ? 轻量<br>? 简单 | ? 功能有限<br>?? 性能一般 | ??? |

**推荐使用 FFmpeg.AutoGen**，因为它最成熟且功能最强大。

---

## ?? 实现方案：使用 FFmpeg.AutoGen

### 1. 安装 NuGet 包

在你的项目中添加：

```xml
<ItemGroup>
  <PackageReference Include="FFmpeg.AutoGen" Version="6.0.0" />
</ItemGroup>
```

### 2. 下载 FFmpeg DLL

从以下地址下载 FFmpeg 共享库（Windows x64）：
- https://github.com/BtbN/FFmpeg-Builds/releases

下载 `ffmpeg-n6.0-latest-win64-gpl-shared-6.0.zip`

解压后将以下 DLL 复制到你的 bin 目录：
```
avcodec-60.dll
avformat-60.dll
avutil-58.dll
swscale-7.dll
swresample-4.dll
```

### 3. 创建 H.264 解码器类

```csharp
using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VideoCamServer.Services
{
    /// <summary>
    /// H.264 视频解码器，使用 FFmpeg
    /// </summary>
    public unsafe class H264Decoder : IDisposable
    {
        private AVCodec* _codec;
        private AVCodecContext* _codecContext;
        private AVFrame* _frame;
        private AVFrame* _frameRGB;
        private AVPacket* _packet;
        private SwsContext* _swsContext;
        
        private byte[] _rgbBuffer;
        private int _width;
        private int _height;
        private bool _isInitialized;
        
        static H264Decoder()
        {
            // 设置 FFmpeg 库路径
            string ffmpegPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, 
                "ffmpeg"
            );
            
            if (System.IO.Directory.Exists(ffmpegPath))
            {
                ffmpeg.RootPath = ffmpegPath;
            }
        }
        
        public H264Decoder(int width, int height)
        {
            _width = width;
            _height = height;
            Initialize();
        }
        
        private void Initialize()
        {
            try
            {
                // 查找 H.264 解码器
                _codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
                if (_codec == null)
                {
                    throw new Exception("H.264 解码器未找到");
                }
                
                // 创建解码器上下文
                _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
                if (_codecContext == null)
                {
                    throw new Exception("无法分配解码器上下文");
                }
                
                // 配置解码器参数
                _codecContext->width = _width;
                _codecContext->height = _height;
                _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                
                // 打开解码器
                int ret = ffmpeg.avcodec_open2(_codecContext, _codec, null);
                if (ret < 0)
                {
                    throw new Exception($"无法打开解码器: {GetFFmpegError(ret)}");
                }
                
                // 分配帧
                _frame = ffmpeg.av_frame_alloc();
                _frameRGB = ffmpeg.av_frame_alloc();
                
                // 分配 RGB 缓冲区
                int numBytes = ffmpeg.av_image_get_buffer_size(
                    AVPixelFormat.AV_PIX_FMT_BGRA,
                    _width,
                    _height,
                    1
                );
                _rgbBuffer = new byte[numBytes];
                
                fixed (byte* ptr = _rgbBuffer)
                {
                    ffmpeg.av_image_fill_arrays(
                        ref _frameRGB->data_0,
                        ref _frameRGB->linesize_0,
                        ptr,
                        AVPixelFormat.AV_PIX_FMT_BGRA,
                        _width,
                        _height,
                        1
                    );
                }
                
                // 创建图像转换上下文（YUV420P -> BGRA）
                _swsContext = ffmpeg.sws_getContext(
                    _width,
                    _height,
                    AVPixelFormat.AV_PIX_FMT_YUV420P,
                    _width,
                    _height,
                    AVPixelFormat.AV_PIX_FMT_BGRA,
                    ffmpeg.SWS_BILINEAR,
                    null,
                    null,
                    null
                );
                
                if (_swsContext == null)
                {
                    throw new Exception("无法创建图像转换上下文");
                }
                
                // 分配数据包
                _packet = ffmpeg.av_packet_alloc();
                
                _isInitialized = true;
                Console.WriteLine($"[H264Decoder] 初始化成功 ({_width}x{_height})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H264Decoder] 初始化失败: {ex.Message}");
                Cleanup();
                throw;
            }
        }
        
        /// <summary>
        /// 解码 H.264 数据包
        /// </summary>
        /// <param name="h264Data">H.264 编码的数据</param>
        /// <returns>解码后的 BGRA 格式帧，如果未产生完整帧则返回 null</returns>
        public byte[] Decode(byte[] h264Data)
        {
            if (!_isInitialized || h264Data == null || h264Data.Length == 0)
            {
                return null;
            }
            
            try
            {
                // 设置数据包数据
                fixed (byte* dataPtr = h264Data)
                {
                    _packet->data = dataPtr;
                    _packet->size = h264Data.Length;
                    
                    // 发送数据包到解码器
                    int ret = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                    if (ret < 0)
                    {
                        Console.WriteLine($"[H264Decoder] 发送数据包失败: {GetFFmpegError(ret)}");
                        return null;
                    }
                    
                    // 接收解码后的帧
                    ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                    {
                        // 需要更多数据或已到达流末尾
                        return null;
                    }
                    else if (ret < 0)
                    {
                        Console.WriteLine($"[H264Decoder] 接收帧失败: {GetFFmpegError(ret)}");
                        return null;
                    }
                    
                    // 转换 YUV -> BGRA
                    fixed (byte* rgbPtr = _rgbBuffer)
                    {
                        ffmpeg.sws_scale(
                            _swsContext,
                            _frame->data,
                            _frame->linesize,
                            0,
                            _height,
                            _frameRGB->data,
                            _frameRGB->linesize
                        );
                    }
                    
                    // 创建输出缓冲区副本
                    byte[] output = new byte[_rgbBuffer.Length];
                    Array.Copy(_rgbBuffer, output, _rgbBuffer.Length);
                    
                    return output;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H264Decoder] 解码失败: {ex.Message}");
                return null;
            }
            finally
            {
                // 重置数据包
                ffmpeg.av_packet_unref(_packet);
            }
        }
        
        /// <summary>
        /// 刷新解码器缓冲区
        /// </summary>
        public byte[] Flush()
        {
            if (!_isInitialized)
                return null;
            
            // 发送 null 数据包以刷新解码器
            int ret = ffmpeg.avcodec_send_packet(_codecContext, null);
            if (ret < 0)
                return null;
            
            // 接收剩余的帧
            ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (ret < 0)
                return null;
            
            // 转换并返回
            fixed (byte* rgbPtr = _rgbBuffer)
            {
                ffmpeg.sws_scale(
                    _swsContext,
                    _frame->data,
                    _frame->linesize,
                    0,
                    _height,
                    _frameRGB->data,
                    _frameRGB->linesize
                );
            }
            
            byte[] output = new byte[_rgbBuffer.Length];
            Array.Copy(_rgbBuffer, output, _rgbBuffer.Length);
            return output;
        }
        
        private void Cleanup()
        {
            if (_swsContext != null)
            {
                ffmpeg.sws_freeContext(_swsContext);
                _swsContext = null;
            }
            
            if (_packet != null)
            {
                fixed (AVPacket** ptr = &_packet)
                {
                    ffmpeg.av_packet_free(ptr);
                }
                _packet = null;
            }
            
            if (_frameRGB != null)
            {
                fixed (AVFrame** ptr = &_frameRGB)
                {
                    ffmpeg.av_frame_free(ptr);
                }
                _frameRGB = null;
            }
            
            if (_frame != null)
            {
                fixed (AVFrame** ptr = &_frame)
                {
                    ffmpeg.av_frame_free(ptr);
                }
                _frame = null;
            }
            
            if (_codecContext != null)
            {
                fixed (AVCodecContext** ptr = &_codecContext)
                {
                    ffmpeg.avcodec_free_context(ptr);
                }
                _codecContext = null;
            }
            
            _isInitialized = false;
        }
        
        private static string GetFFmpegError(int error)
        {
            byte[] buffer = new byte[1024];
            ffmpeg.av_strerror(error, buffer, (ulong)buffer.Length);
            return System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0');
        }
        
        public void Dispose()
        {
            Cleanup();
            Console.WriteLine("[H264Decoder] 已释放资源");
        }
    }
}
```

### 4. 更新 VideoProcessor 使用真实解码器

```csharp
public class VideoProcessor
{
    private const int VideoWidth = 1920;
    private const int VideoHeight = 1080;

    public event FrameReadyEventHandler FrameReady;

    private H264Decoder _decoder;
    private VirtualCamera _virtualCamera;
    private readonly object _cameraLock = new object();
    private readonly object _decoderLock = new object();

    public VideoProcessor()
    {
        InitializeDecoder();
    }

    private void InitializeDecoder()
    {
        Console.WriteLine("[VideoProcessor] 正在初始化 H.264 解码器...");
        
        try
        {
            _decoder = new H264Decoder(VideoWidth, VideoHeight);
            Console.WriteLine("[VideoProcessor] H.264 解码器初始化完成。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoProcessor] 解码器初始化失败: {ex.Message}");
            _decoder = null;
        }
    }

    private void EnsureVirtualCameraInitialized()
    {
        if (_virtualCamera == null)
        {
            lock (_cameraLock)
            {
                if (_virtualCamera == null)
                {
                    _virtualCamera = new VirtualCamera(VideoWidth, VideoHeight, 30);
                }
            }
        }
    }

    public void ProcessUdpPacket(byte[] data)
    {
        if (_decoder == null || data == null || data.Length == 0)
            return;

        try
        {
            lock (_decoderLock)
            {
                // 解码 H.264 数据
                byte[] decodedFrame = _decoder.Decode(data);
                
                if (decodedFrame != null)
                {
                    // 触发帧就绪事件（用于 UI 预览等）
                    FrameReady?.Invoke(decodedFrame, VideoWidth, VideoHeight);

                    // 确保虚拟摄像头已初始化
                    EnsureVirtualCameraInitialized();
                    
                    // 推送到虚拟摄像头
                    _virtualCamera?.PushFrame(decodedFrame);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoProcessor] 处理 UDP 包失败: {ex.Message}");
        }
    }

    public void Shutdown()
    {
        Console.WriteLine("[VideoProcessor] 正在关闭解码器并释放资源...");
        
        _decoder?.Dispose();
        _decoder = null;
        
        _virtualCamera?.Shutdown();
        _virtualCamera = null;
        
        Console.WriteLine("[VideoProcessor] 资源已释放。");
    }
}
```

---

## ?? 部署步骤

### 1. 安装 FFmpeg.AutoGen

```powershell
cd D:\job\VideoCam\VideoCamServer
dotnet add package FFmpeg.AutoGen --version 6.0.0
```

### 2. 下载 FFmpeg DLL

1. 访问: https://github.com/BtbN/FFmpeg-Builds/releases
2. 下载: `ffmpeg-n6.0-latest-win64-gpl-shared-6.0.zip`
3. 解压到临时目录
4. 在项目目录创建 `ffmpeg` 文件夹：
   ```
   D:\job\VideoCam\VideoCamServer\bin\Debug\net5.0-windows\ffmpeg\
   ```
5. 复制以下 DLL 到该文件夹：
   - avcodec-60.dll
   - avformat-60.dll
   - avutil-58.dll
   - swscale-7.dll
   - swresample-4.dll

### 3. 项目文件配置（可选 - 自动复制 DLL）

在 `.csproj` 文件中添加：

```xml
<ItemGroup>
  <None Include="ffmpeg\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

---

## ?? 测试

### 测试步骤

1. 启动服务器
2. 从手机端发送 H.264 数据
3. 查看日志输出：
   ```
   [VideoProcessor] 正在初始化 H.264 解码器...
   [H264Decoder] 初始化成功 (1920x1080)
   [VirtualCamera] OBS 虚拟摄像头已启动 (1920x1080 @ 30fps)
   ```

### 常见问题

#### 1. DLL 找不到
```
无法加载 DLL 'avcodec-60.dll': 找不到指定的模块
```

**解决方案**:
- 确保所有 DLL 在 `ffmpeg` 文件夹中
- 检查 DLL 架构（必须是 x64）
- 使用 Dependency Walker 检查依赖

#### 2. 解码器初始化失败
```
H.264 解码器未找到
```

**解决方案**:
- 确认 FFmpeg DLL 版本兼容
- 检查 DLL 是否损坏
- 重新下载 FFmpeg 构建

#### 3. 解码失败
```
接收帧失败: [错误信息]
```

**解决方案**:
- 检查手机端编码参数
- 确认数据包完整性
- 查看 FFmpeg 错误信息

---

## ?? 性能优化

### 1. 多线程解码

如果需要处理高帧率（60fps+），可以使用多线程：

```csharp
_codecContext->thread_count = Environment.ProcessorCount;
_codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;
```

### 2. 硬件加速

使用 NVIDIA NVDEC 或 Intel Quick Sync：

```csharp
// NVIDIA
_codec = ffmpeg.avcodec_find_decoder_by_name("h264_cuvid");

// Intel
_codec = ffmpeg.avcodec_find_decoder_by_name("h264_qsv");
```

### 3. 减少内存分配

重用缓冲区而不是每次创建新的：

```csharp
private byte[] _outputBuffer = new byte[_width * _height * 4];

public byte[] Decode(byte[] h264Data)
{
    // ... 解码逻辑 ...
    Array.Copy(_rgbBuffer, _outputBuffer, _rgbBuffer.Length);
    return _outputBuffer;
}
```

---

## ?? 备用方案：Windows Media Foundation

如果 FFmpeg 有问题，可以使用 Windows 原生解码器：

```csharp
// 需要添加 Windows SDK 引用
using Windows.Media.Core;
using Windows.Media.MediaProperties;

// 实现略复杂，但无需额外 DLL
```

---

## ? 完成清单

- [ ] 安装 FFmpeg.AutoGen NuGet 包
- [ ] 下载并放置 FFmpeg DLL
- [ ] 创建 H264Decoder 类
- [ ] 更新 VideoProcessor
- [ ] 测试解码功能
- [ ] 验证虚拟摄像头输出
- [ ] 性能调优

需要我帮你创建 H264Decoder.cs 文件并集成到项目中吗？
