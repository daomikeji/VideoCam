using System;
using VideoCamServer.Services;

namespace VideoCamServer.Examples
{
    /// <summary>
    /// OBS Virtual Camera 使用示例
    /// 演示如何在实际项目中使用虚拟摄像头功能
    /// </summary>
    public class VirtualCameraUsageExample
    {
        /// <summary>
        /// 示例 1：基本使用
        /// 创建虚拟摄像头并推送测试帧
        /// </summary>
        public static void Example1_BasicUsage()
        {
            Console.WriteLine("=== 示例 1：基本使用 ===\n");

            // 创建虚拟摄像头 (1920x1080 @ 30fps)
            using (var camera = new VirtualCamera(1920, 1080, 30))
            {
                if (camera.IsAvailable)
                {
                    Console.WriteLine("虚拟摄像头已启动");

                    // 创建测试帧（黑色帧）
                    byte[] testFrame = CreateSolidColorFrame(1920, 1080, 0, 0, 0);

                    // 推送 100 帧
                    for (int i = 0; i < 100; i++)
                    {
                        camera.PushFrame(testFrame);
                        System.Threading.Thread.Sleep(33); // ~30fps
                        Console.Write($"\r推送帧: {i + 1}/100");
                    }

                    Console.WriteLine("\n完成！");
                }
                else
                {
                    Console.WriteLine("虚拟摄像头启动失败");
                }
            }
        }

        /// <summary>
        /// 示例 2：推送彩色测试帧
        /// 演示如何创建和推送不同颜色的帧
        /// </summary>
        public static void Example2_ColorFrames()
        {
            Console.WriteLine("\n=== 示例 2：彩色测试帧 ===\n");

            using (var camera = new VirtualCamera(1280, 720, 15))
            {
                if (!camera.IsAvailable)
                {
                    Console.WriteLine("虚拟摄像头启动失败");
                    return;
                }

                // 红色帧
                Console.WriteLine("推送红色帧...");
                byte[] redFrame = CreateSolidColorFrame(1280, 720, 0, 0, 255);
                for (int i = 0; i < 15; i++)
                {
                    camera.PushFrame(redFrame);
                    System.Threading.Thread.Sleep(66);
                }

                // 绿色帧
                Console.WriteLine("推送绿色帧...");
                byte[] greenFrame = CreateSolidColorFrame(1280, 720, 0, 255, 0);
                for (int i = 0; i < 15; i++)
                {
                    camera.PushFrame(greenFrame);
                    System.Threading.Thread.Sleep(66);
                }

                // 蓝色帧
                Console.WriteLine("推送蓝色帧...");
                byte[] blueFrame = CreateSolidColorFrame(1280, 720, 255, 0, 0);
                for (int i = 0; i < 15; i++)
                {
                    camera.PushFrame(blueFrame);
                    System.Threading.Thread.Sleep(66);
                }

                Console.WriteLine("完成！");
            }
        }

        /// <summary>
        /// 示例 3：格式转换
        /// 演示如何将不同格式的帧转换为 BGRA
        /// </summary>
        public static void Example3_FormatConversion()
        {
            Console.WriteLine("\n=== 示例 3：格式转换 ===\n");

            int width = 1280;
            int height = 720;

            using (var camera = new VirtualCamera(width, height, 30))
            {
                if (!camera.IsAvailable)
                {
                    Console.WriteLine("虚拟摄像头启动失败");
                    return;
                }

                // 模拟 NV12 格式的数据
                Console.WriteLine("模拟 NV12 格式数据...");
                byte[] nv12Data = CreateMockNV12Frame(width, height);
                
                // 检测格式
                string detectedFormat = VideoFormatConverter.DetectFormat(nv12Data, width, height);
                Console.WriteLine($"检测到的格式: {detectedFormat}");

                // 转换为 BGRA
                Console.WriteLine("转换为 BGRA 格式...");
                byte[] bgraData = VideoFormatConverter.ConvertNV12ToBGRA(nv12Data, width, height);

                // 推送帧
                Console.WriteLine("推送转换后的帧...");
                for (int i = 0; i < 30; i++)
                {
                    camera.PushFrame(bgraData);
                    System.Threading.Thread.Sleep(33);
                    Console.Write($"\r帧: {i + 1}/30");
                }

                Console.WriteLine("\n完成！");
            }
        }

        /// <summary>
        /// 示例 4：多线程推送
        /// 演示如何在多线程环境中使用虚拟摄像头
        /// </summary>
        public static void Example4_MultiThreading()
        {
            Console.WriteLine("\n=== 示例 4：多线程推送 ===\n");

            using (var camera = new VirtualCamera(640, 480, 30))
            {
                if (!camera.IsAvailable)
                {
                    Console.WriteLine("虚拟摄像头启动失败");
                    return;
                }

                bool isRunning = true;
                int frameCount = 0;

                // 创建生产者线程（模拟视频解码）
                var producerThread = new System.Threading.Thread(() =>
                {
                    while (isRunning)
                    {
                        byte[] frame = CreateSolidColorFrame(640, 480, 
                            (byte)(frameCount % 256), 
                            (byte)((frameCount * 2) % 256), 
                            (byte)((frameCount * 3) % 256));

                        camera.PushFrame(frame);
                        frameCount++;

                        System.Threading.Thread.Sleep(33); // ~30fps
                    }
                });

                Console.WriteLine("开始推送帧（按任意键停止）...");
                producerThread.Start();

                Console.ReadKey();
                isRunning = false;
                producerThread.Join();

                Console.WriteLine($"\n总共推送了 {frameCount} 帧");
            }
        }

        /// <summary>
        /// 示例 5：错误处理
        /// 演示如何处理各种错误情况
        /// </summary>
        public static void Example5_ErrorHandling()
        {
            Console.WriteLine("\n=== 示例 5：错误处理 ===\n");

            try
            {
                // 测试无效参数
                Console.WriteLine("测试 1: 无效的分辨率...");
                try
                {
                    var camera = new VirtualCamera(-1, 1080, 30);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    Console.WriteLine($"捕获到预期的异常: {ex.Message}");
                }

                // 测试推送空帧
                Console.WriteLine("\n测试 2: 推送空帧...");
                using (var camera = new VirtualCamera(640, 480, 30))
                {
                    try
                    {
                        camera.PushFrame(null);
                    }
                    catch (ArgumentNullException ex)
                    {
                        Console.WriteLine($"捕获到预期的异常: {ex.Message}");
                    }
                }

                // 测试推送到已关闭的摄像头
                Console.WriteLine("\n测试 3: 推送到已关闭的摄像头...");
                var closedCamera = new VirtualCamera(640, 480, 30);
                closedCamera.Shutdown();
                try
                {
                    byte[] frame = CreateSolidColorFrame(640, 480, 0, 0, 0);
                    closedCamera.PushFrame(frame);
                }
                catch (ObjectDisposedException ex)
                {
                    Console.WriteLine($"捕获到预期的异常: {ex.Message}");
                }

                // 测试错误的帧大小
                Console.WriteLine("\n测试 4: 错误的帧大小...");
                using (var camera = new VirtualCamera(640, 480, 30))
                {
                    if (camera.IsAvailable)
                    {
                        // 推送错误大小的帧（应该被忽略或处理）
                        byte[] wrongSizeFrame = new byte[1000];
                        camera.PushFrame(wrongSizeFrame);
                        Console.WriteLine("推送了错误大小的帧（可能会被忽略）");
                    }
                }

                Console.WriteLine("\n所有错误处理测试完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"未预期的异常: {ex}");
            }
        }

        // ==================== 辅助方法 ====================

        /// <summary>
        /// 创建纯色帧（BGRA 格式）
        /// </summary>
        private static byte[] CreateSolidColorFrame(int width, int height, byte b, byte g, byte r)
        {
            byte[] frame = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                int index = i * 4;
                frame[index] = b;       // B
                frame[index + 1] = g;   // G
                frame[index + 2] = r;   // R
                frame[index + 3] = 255; // A
            }
            return frame;
        }

        /// <summary>
        /// 创建模拟的 NV12 格式帧
        /// </summary>
        private static byte[] CreateMockNV12Frame(int width, int height)
        {
            // NV12 格式: Y 平面 + UV 交错平面
            int ySize = width * height;
            int uvSize = width * height / 2;
            byte[] nv12 = new byte[ySize + uvSize];

            // 填充 Y 平面（灰度值）
            for (int i = 0; i < ySize; i++)
            {
                nv12[i] = 128; // 中等亮度
            }

            // 填充 UV 平面（色度值）
            for (int i = 0; i < uvSize; i++)
            {
                nv12[ySize + i] = 128; // 中性色度
            }

            return nv12;
        }

        /// <summary>
        /// 运行所有示例
        /// </summary>
        public static void RunAllExamples()
        {
            Console.WriteLine("╔═══════════════════════════════════════════╗");
            Console.WriteLine("║  OBS Virtual Camera 使用示例集合          ║");
            Console.WriteLine("╚═══════════════════════════════════════════╝\n");

            // Example1_BasicUsage();
            // Example2_ColorFrames();
            // Example3_FormatConversion();
            // Example4_MultiThreading();
            Example5_ErrorHandling();

            Console.WriteLine("\n所有示例运行完成！");
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}
