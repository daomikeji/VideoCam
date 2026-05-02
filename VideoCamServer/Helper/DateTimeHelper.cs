using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoCamServer.Helper
{
   public class DateTimeHelper
    {
        /// <summary> 
        /// 获取时间戳(秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static long GetTimeStampS(DateTime dateTime)
        {
            TimeSpan ts = dateTime.ToUniversalTime()  - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return (long)ts.TotalSeconds;
        }
        /// <summary> 
        /// 获取时间戳(秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static long GetTimeStampS()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0,DateTimeKind.Utc);
            return  (long)ts.TotalSeconds;
        }
        /// <summary> 
        /// 获取时间戳(毫秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static long GetTimeStampMS()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return (long)ts.TotalMilliseconds;
        }
        /// <summary> 
        /// 获取时间戳(毫秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static long GetTimeStampMS(DateTime dateTime)
        {
            TimeSpan ts = dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return (long)ts.TotalMilliseconds;
        }
        /// <summary> 
        /// 获取时间(毫秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static DateTime GetDateTimeMS(long timeStamp)
        {
           return  new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timeStamp).ToLocalTime();
            //TimeSpan ts = dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            //return (long)ts.TotalMilliseconds;
        }
        /// <summary> 
        /// 获取时间(秒) 
        /// </summary> 
        /// <returns>UTC</returns> 
        public static DateTime GetDateTimeS(long timeStamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timeStamp).ToLocalTime();
            //TimeSpan ts = dateTime.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            //return (long)ts.TotalMilliseconds;
        }
        public static string GetStringTimeMS()
        {
            return DateTime.Now.ToString("yyyyMMddHHmmssfff");
        }
        public static string GetStringTimeMS(DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
        /// <summary>
        /// 判断两个日期是否在同一周
        /// </summary>
        /// <param name="dtmS">开始日期</param>
        /// <param name="dtmE">结束日期</param>
        /// <returns></returns>
        public static bool IsWeek(DateTime dtmS, DateTime dtmE)
        {
            //TimeSpan ts = dtmE > dtmS ? dtmE - dtmS : dtmS - dtmE;
            //double dbl = ts.TotalDays;
            int intS = (int)dtmS.DayOfWeek;
            int intE = (int)dtmE.DayOfWeek;
            if (intS == 0)
            {
                intS = 7;
            }
            if (intE == 0)
            {
                intE = 7;
            }
            if (intE > 1)
            {
                dtmE= dtmE.AddDays(-(intE - 1));
            }
            if (intS > 1)
            {
                dtmS= dtmS.AddDays(-(intS - 1));
            }
            if (dtmS.Date==dtmE.Date)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}

