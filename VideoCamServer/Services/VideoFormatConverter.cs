using System;
using System.Runtime.CompilerServices;

namespace VideoCamServer.Services
{
    /// <summary>
    /// 视频格式转换工具类
    /// 提供常见视频格式之间的转换功能
    /// </summary>
    public static class VideoFormatConverter
    {
        /// <summary>
        /// 将 NV12 格式转换为 BGRA 格式
        /// NV12 是一种常见的 YUV420 格式，常用于移动设备
        /// </summary>
        /// <param name="nv12Data">NV12 格式的源数据</param>
        /// <param name="width">视频宽度</param>
        /// <param name="height">视频高度</param>
        /// <returns>BGRA 格式的数据</returns>
        public static byte[] ConvertNV12ToBGRA(byte[] nv12Data, int width, int height)
        {
            if (nv12Data == null)
                throw new ArgumentNullException(nameof(nv12Data));

            int expectedSize = width * height * 3 / 2; // NV12 size
            if (nv12Data.Length < expectedSize)
                throw new ArgumentException($"NV12 数据大小不足，期望至少 {expectedSize} 字节，实际 {nv12Data.Length} 字节");

            byte[] bgraData = new byte[width * height * 4];
            
            int yPlaneSize = width * height;
            int uvPlaneSize = width * height / 2;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = y * width + x;
                    int uvIndex = yPlaneSize + (y / 2) * width + (x & ~1);

                    // 获取 YUV 值
                    int yVal = nv12Data[yIndex] & 0xFF;
                    int uVal = nv12Data[uvIndex] & 0xFF;
                    int vVal = nv12Data[uvIndex + 1] & 0xFF;

                    // YUV 转 RGB
                    int c = yVal - 16;
                    int d = uVal - 128;
                    int e = vVal - 128;

                    int r = Clamp((298 * c + 409 * e + 128) >> 8);
                    int g = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
                    int b = Clamp((298 * c + 516 * d + 128) >> 8);

                    // 写入 BGRA
                    int bgraIndex = (y * width + x) * 4;
                    bgraData[bgraIndex] = (byte)b;     // B
                    bgraData[bgraIndex + 1] = (byte)g; // G
                    bgraData[bgraIndex + 2] = (byte)r; // R
                    bgraData[bgraIndex + 3] = 255;     // A
                }
            }

            return bgraData;
        }

        /// <summary>
        /// 将 I420 (YUV420P) 格式转换为 BGRA 格式
        /// </summary>
        /// <param name="i420Data">I420 格式的源数据</param>
        /// <param name="width">视频宽度</param>
        /// <param name="height">视频高度</param>
        /// <returns>BGRA 格式的数据</returns>
        public static byte[] ConvertI420ToBGRA(byte[] i420Data, int width, int height)
        {
            if (i420Data == null)
                throw new ArgumentNullException(nameof(i420Data));

            int expectedSize = width * height * 3 / 2;
            if (i420Data.Length < expectedSize)
                throw new ArgumentException($"I420 数据大小不足，期望至少 {expectedSize} 字节，实际 {i420Data.Length} 字节");

            byte[] bgraData = new byte[width * height * 4];

            int yPlaneSize = width * height;
            int uPlaneSize = width * height / 4;
            int vPlaneSize = width * height / 4;

            int yOffset = 0;
            int uOffset = yPlaneSize;
            int vOffset = yPlaneSize + uPlaneSize;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = yOffset + y * width + x;
                    int uvIndex = (y / 2) * (width / 2) + (x / 2);

                    // 获取 YUV 值
                    int yVal = i420Data[yIndex] & 0xFF;
                    int uVal = i420Data[uOffset + uvIndex] & 0xFF;
                    int vVal = i420Data[vOffset + uvIndex] & 0xFF;

                    // YUV 转 RGB
                    int c = yVal - 16;
                    int d = uVal - 128;
                    int e = vVal - 128;

                    int r = Clamp((298 * c + 409 * e + 128) >> 8);
                    int g = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
                    int b = Clamp((298 * c + 516 * d + 128) >> 8);

                    // 写入 BGRA
                    int bgraIndex = (y * width + x) * 4;
                    bgraData[bgraIndex] = (byte)b;     // B
                    bgraData[bgraIndex + 1] = (byte)g; // G
                    bgraData[bgraIndex + 2] = (byte)r; // R
                    bgraData[bgraIndex + 3] = 255;     // A
                }
            }

            return bgraData;
        }

        /// <summary>
        /// 将 RGB24 格式转换为 BGRA 格式
        /// </summary>
        /// <param name="rgb24Data">RGB24 格式的源数据</param>
        /// <param name="width">视频宽度</param>
        /// <param name="height">视频高度</param>
        /// <returns>BGRA 格式的数据</returns>
        public static byte[] ConvertRGB24ToBGRA(byte[] rgb24Data, int width, int height)
        {
            if (rgb24Data == null)
                throw new ArgumentNullException(nameof(rgb24Data));

            int expectedSize = width * height * 3;
            if (rgb24Data.Length < expectedSize)
                throw new ArgumentException($"RGB24 数据大小不足，期望至少 {expectedSize} 字节，实际 {rgb24Data.Length} 字节");

            byte[] bgraData = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                int rgbIndex = i * 3;
                int bgraIndex = i * 4;

                bgraData[bgraIndex] = rgb24Data[rgbIndex + 2];     // B
                bgraData[bgraIndex + 1] = rgb24Data[rgbIndex + 1]; // G
                bgraData[bgraIndex + 2] = rgb24Data[rgbIndex];     // R
                bgraData[bgraIndex + 3] = 255;                      // A
            }

            return bgraData;
        }

        /// <summary>
        /// 将 RGBA 格式转换为 BGRA 格式
        /// </summary>
        /// <param name="rgbaData">RGBA 格式的源数据</param>
        /// <param name="width">视频宽度</param>
        /// <param name="height">视频高度</param>
        /// <returns>BGRA 格式的数据</returns>
        public static byte[] ConvertRGBAToBGRA(byte[] rgbaData, int width, int height)
        {
            if (rgbaData == null)
                throw new ArgumentNullException(nameof(rgbaData));

            int expectedSize = width * height * 4;
            if (rgbaData.Length < expectedSize)
                throw new ArgumentException($"RGBA 数据大小不足，期望至少 {expectedSize} 字节，实际 {rgbaData.Length} 字节");

            byte[] bgraData = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                int index = i * 4;
                bgraData[index] = rgbaData[index + 2];     // B
                bgraData[index + 1] = rgbaData[index + 1]; // G
                bgraData[index + 2] = rgbaData[index];     // R
                bgraData[index + 3] = rgbaData[index + 3]; // A
            }

            return bgraData;
        }

        /// <summary>
        /// 限制值在 0-255 范围内
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        /// <summary>
        /// 检测视频数据格式（简单启发式方法）
        /// </summary>
        /// <param name="data">视频数据</param>
        /// <param name="width">视频宽度</param>
        /// <param name="height">视频高度</param>
        /// <returns>可能的格式名称</returns>
        public static string DetectFormat(byte[] data, int width, int height)
        {
            if (data == null || data.Length == 0)
                return "UNKNOWN";

            int pixels = width * height;
            
            // BGRA/RGBA: 4 bytes per pixel
            if (data.Length == pixels * 4)
                return "BGRA/RGBA";
            
            // RGB24: 3 bytes per pixel
            if (data.Length == pixels * 3)
                return "RGB24";
            
            // NV12/I420: 1.5 bytes per pixel
            if (data.Length == pixels * 3 / 2)
                return "NV12/I420";
            
            // YUV422: 2 bytes per pixel
            if (data.Length == pixels * 2)
                return "YUV422";

            return "UNKNOWN";
        }
    }
}
