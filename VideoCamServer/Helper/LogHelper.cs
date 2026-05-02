
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VideoCamServer.Helper
{
    public class LogHelper
    {
        private static object logLock = new object();
        private const long MaxLogFileSize = 10 * 1024 * 1024;
        private static readonly Dictionary<string, string> currentLogFileCache = new Dictionary<string, string>();

        private static string GetLogRootPath()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(basePath, "log");
                Directory.CreateDirectory(path);
                return path;
            }
            catch
            {
                string programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "S7WCS", "log");
                Directory.CreateDirectory(programDataPath);
                return programDataPath;
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Info";
            }
            return Regex.Replace(name, "[\\\\/:*?\"<>|]", "_");
        }



        #region 文件路径信息
        /// <summary>
        /// 获取当前路径(相对应程序)信息,支持文件夹
        /// </summary>
        /// <param name="path">短路径(前无\\)</param>
        /// <returns>全路径</returns>
        public static string GetCurrentPath(string path)
        {

            path = Environment.CurrentDirectory + "\\" + path;
            bool directoryFlag = Directory.Exists(path);
            if (directoryFlag)
            {
                return path;
            }
            else
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
        /// <summary>
        /// 获取当前路径(相对应程序)信息,支持文件
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string GetCurrentFilePath(string FilePath)
        {
            bool fileFlag = File.Exists(FilePath);
            if (fileFlag)
            {
                return FilePath;
            }
            else
            {
                try
                {
                    File.Create(FilePath);
                    return FilePath;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public static string[] GetCurrentFile(string path)
        {
            if (Directory.Exists(path))
            {
                return Directory.GetFiles(path);
            }
            else
            {
                Directory.CreateDirectory(path);
            }
            return null;
        }
        /// <summary>
        /// 判断文件是否存在
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool FileExists(string path)
        {
            if (File.Exists(path))
            {
                return true;
            }
            return false;
        }
        public static void DeleteFile(string path)
        {
            path = AppDomain.CurrentDomain + "\\" + path;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        /// <summary>
        /// 获取当前路径下的文件夹
        /// </summary>
        /// <param name="path">指定路径</param>
        /// <returns></returns>
        public static string[] GetCurrentDirectorise(string path)
        {
            if (Directory.Exists(path))
            {
                return Directory.GetDirectories(path);
            }
            else
            {
                Directory.CreateDirectory(path);
            }
            return null;
        }
        #endregion
        #region 读写日志文件
        //private static object LogLock = new object();
        public static void WriteInfoLog(string content)
        {
            WriteLog("Info", content);
        }
        public static void WriteExceptionLog(string content)
        {
            WriteLog("Exception", content);
        }

        /// <summary>
        /// 按业务类型记录日志
        /// </summary>
        /// <param name="businessName">分拣/设备状态/亮灯/按钮 等</param>
        /// <param name="content"></param>
        public static void WriteBusinessLog(string businessName, string content)
        {
            WriteLog(businessName, content);
        }
        public static void WriteException(string businessName, string message, Exception ex)
        {
            string fullMessage = message + ": " + ex.Message;
            LogHelper.WriteBusinessLog(businessName, fullMessage);
            LogHelper.WriteExceptionLog("[" + businessName + "] " + fullMessage + "\r\n" + ex.ToString());
        }
        private static string BuildLogFilePath(string folderPath, string fileType, int index)
        {
            if (index <= 0)
            {
                return folderPath + "\\" + fileType + ".dat";
            }
            return folderPath + "\\" + fileType + "-" + index + ".dat";
        }

        private static int GetNextIndexFromPath(string currentPath, string fileType)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(currentPath);
                string prefix = fileType + "-";
                if (fileName != null && fileName.StartsWith(prefix))
                {
                    string idx = fileName.Substring(prefix.Length);
                    int parsed;
                    if (int.TryParse(idx, out parsed))
                    {
                        return parsed + 1;
                    }
                }
            }
            catch
            {
            }
            return 1;
        }

        private static string GetWriteLogPath(string folderPath, string fileType)
        {
            string cacheKey = folderPath + "|" + fileType;
            string cachedPath;
            if (currentLogFileCache.TryGetValue(cacheKey, out cachedPath) && File.Exists(cachedPath))
            {
                try
                {
                    if (new FileInfo(cachedPath).Length < MaxLogFileSize)
                    {
                        return cachedPath;
                    }
                }
                catch
                {
                }
            }

            int index = 0;
            if (!string.IsNullOrEmpty(cachedPath))
            {
                index = GetNextIndexFromPath(cachedPath, fileType);
            }

            while (true)
            {
                string tempPath = BuildLogFilePath(folderPath, fileType, index);
                if (!File.Exists(tempPath))
                {
                    currentLogFileCache[cacheKey] = tempPath;
                    return tempPath;
                }

                try
                {
                    if (new FileInfo(tempPath).Length < MaxLogFileSize)
                    {
                        currentLogFileCache[cacheKey] = tempPath;
                        return tempPath;
                    }
                }
                catch
                {
                    currentLogFileCache[cacheKey] = tempPath;
                    return tempPath;
                }

                index++;
            }
        }
        /// <summary>
        /// 写日志
        /// </summary>
        /// <param name="Type">类型</param>
        /// <param name="content">内容</param>
        public static void WriteLog(string Type, string content)
        {
            lock (logLock)
            {
                string folderPath = Path.Combine(GetLogRootPath(), DateTime.Now.ToString("yyyyMMdd"));
                string fileType = DateTime.Now.ToString("yyyyMMdd") + SafeFileName(Type);
                try
                {
                    Directory.CreateDirectory(folderPath);
                    string logPath = GetWriteLogPath(folderPath, fileType);
                    using (StreamWriter sw = new StreamWriter(logPath, true, Encoding.Default))
                    {
                        sw.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + content);
                        sw.Flush();
                    }
                }
                catch (Exception e)
                {
                    return;
                }
            }

        }
        /// <summary>
        /// 写入日志文件
        /// </summary>        
        /// <param name="Date1">20160819</param>
        public static string ReadLog(string filename)
        {
            string path = Path.Combine(GetLogRootPath(), filename);
            StringBuilder sb = new StringBuilder();
            try
            {
                if (File.Exists(path))
                {
                    FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                    if (fs.CanRead)
                    {
                        using (StreamReader sr = new StreamReader(fs, Encoding.Default))
                        {
                            while (sr.Peek() > 0)
                            {
                                sb = sb.Append(sr.ReadLine() + "\n");
                            }
                            sr.Close();
                        }
                    }
                    fs.Close();
                    return sb.ToString();
                }
                else
                {
                    return sb.ToString();
                }
            }
            catch
            {
                return sb.ToString();
            }


        }


        #endregion
    }
}

