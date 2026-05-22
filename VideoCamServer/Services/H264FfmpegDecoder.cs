using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VideoCamServer.Services
{
    public unsafe sealed class H264FfmpegDecoder : IDisposable
    {
        private AVCodecContext* _codecContext;
        private AVCodecParserContext* _parserContext;
        private AVPacket* _packet;
        private AVFrame* _decodedFrame;
        private SwsContext* _swsContext;

        private byte_ptrArray4 _dstData;
        private int_array4 _dstLinesize;
        private byte* _dstBuffer;
        private int _dstBufferSize;

        private int _lastWidth;
        private int _lastHeight;
        private AVPixelFormat _lastPixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;

        private AVCodec* _codec; // 保存解码器

        public H264FfmpegDecoder()
        {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            Console.WriteLine("FFmpeg version: " + ffmpeg.av_version_info());

            // 查找解码器
            _codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (_codec == null)
            {
                throw new InvalidOperationException("未找到 H.264 解码器");
            }

            // 初始化解析器
            _parserContext = ffmpeg.av_parser_init((int)AVCodecID.AV_CODEC_ID_H264);
            if (_parserContext == null)
            {
                throw new InvalidOperationException("初始化 H.264 parser 失败");
            }

            // 分配上下文（不打开！）
            _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
            if (_codecContext == null)
            {
                throw new InvalidOperationException("分配解码上下文失败");
            }

            // 配置多线程
            _codecContext->thread_count = Math.Max(1, Environment.ProcessorCount / 2);
            _codecContext->thread_type = ffmpeg.FF_THREAD_FRAME;

            // 分配包和帧
            _packet = ffmpeg.av_packet_alloc();
            _decodedFrame = ffmpeg.av_frame_alloc();

            if (_packet == null || _decodedFrame == null)
            {
                throw new InvalidOperationException("分配 packet/frame 失败");
            }
        }

        /// <summary>
        /// 解码器是否已经打开
        /// </summary>
        private bool IsCodecOpen => _codecContext != null && _codecContext->codec != null;

        public bool TryDecode(byte[] encodedData, out byte[] bgraFrame, out int width, out int height)
        {
            bgraFrame = Array.Empty<byte>();
            width = 0;
            height = 0;

            if (encodedData == null || encodedData.Length == 0)
                return false;

            fixed (byte* encodedPtr = encodedData)
            {
                byte* current = encodedPtr;
                int remaining = encodedData.Length;

                while (remaining > 0)
                {
                    byte* parsedData = null;
                    int parsedSize = 0;

                    int consumed = ffmpeg.av_parser_parse2(
                        _parserContext,
                        _codecContext,
                        &parsedData,
                        &parsedSize,
                        current,
                        remaining,
                        ffmpeg.AV_NOPTS_VALUE,
                        ffmpeg.AV_NOPTS_VALUE,
                        0);

                    if (consumed < 0) return false;
                    current += consumed;
                    remaining -= consumed;
                    if (parsedSize <= 0) continue;

                    // ====================== 关键修复 ======================
                    // 第一次解析出数据后，自动打开解码器
                    if (!IsCodecOpen)
                    {
                        int openRet = ffmpeg.avcodec_open2(_codecContext, _codec, null);
                        if (openRet < 0)
                        {
                            Console.WriteLine($"打开解码器失败: {openRet}");
                            return false;
                        }
                        Console.WriteLine("H264 解码器打开成功！");
                    }
                    // ======================================================

                    // 发送包解码
                    ffmpeg.av_packet_unref(_packet);
                    ffmpeg.av_new_packet(_packet, parsedSize);
                    Buffer.MemoryCopy(parsedData, _packet->data, parsedSize, parsedSize);
                    _packet->size = parsedSize;

                    int sendRet = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                    if (sendRet < 0 && sendRet != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        continue;

                    // 接收帧
                    while (true)
                    {
                        int receiveRet = ffmpeg.avcodec_receive_frame(_codecContext, _decodedFrame);
                        if (receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN) || receiveRet == ffmpeg.AVERROR_EOF)
                            break;
                        if (receiveRet < 0) break;

                        // 转 BGRA
                        bool ok = ConvertToBgra(_decodedFrame, out bgraFrame, out width, out height);
                        ffmpeg.av_frame_unref(_decodedFrame);
                        return ok;
                    }
                }
            }

            return false;
        }

        private bool ConvertToBgra(AVFrame* frame, out byte[] bgra, out int w, out int h)
        {
            bgra = Array.Empty<byte>();
            w = frame->width;
            h = frame->height;

            if (w <= 0 || h <= 0) return false;
            AVPixelFormat fmt = (AVPixelFormat)frame->format;

            // 重建转换上下文
            if (_swsContext == null || w != _lastWidth || h != _lastHeight || fmt != _lastPixelFormat)
            {
                if (_swsContext != null) ffmpeg.sws_freeContext(_swsContext);
                _swsContext = ffmpeg.sws_getContext(
                    w, h, fmt,
                    w, h, AVPixelFormat.AV_PIX_FMT_BGRA,
                    ffmpeg.SWS_BILINEAR, null, null, null);

                if (_swsContext == null) return false;

                if (_dstBuffer != null) ffmpeg.av_free(_dstBuffer);
                _dstBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, w, h, 1);
                _dstBuffer = (byte*)ffmpeg.av_malloc((ulong)_dstBufferSize);
                ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLinesize, _dstBuffer, AVPixelFormat.AV_PIX_FMT_BGRA, w, h, 1);

                _lastWidth = w;
                _lastHeight = h;
                _lastPixelFormat = fmt;
            }

            // 缩放转换
            ffmpeg.sws_scale(_swsContext,
                frame->data, frame->linesize, 0, h,
                _dstData, _dstLinesize);

            // 复制到托管数组
            bgra = new byte[w * h * 4];
            Marshal.Copy((IntPtr)_dstData[0], bgra, 0, bgra.Length);
            return true;
        }

        public void Dispose()
        {
            if (_dstBuffer != null) { ffmpeg.av_free(_dstBuffer); _dstBuffer = null; }
            if (_swsContext != null) { ffmpeg.sws_freeContext(_swsContext); _swsContext = null; }
            if (_decodedFrame != null) { AVFrame* p = _decodedFrame; ffmpeg.av_frame_free(&p); _decodedFrame = null; }
            if (_packet != null) { AVPacket* p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
            if (_codecContext != null) { AVCodecContext* p = _codecContext; ffmpeg.avcodec_free_context(&p); _codecContext = null; }
            if (_parserContext != null) { ffmpeg.av_parser_close(_parserContext); _parserContext = null; }
        }
    }
}