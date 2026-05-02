using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VideoCamServer.Helper
{
    public class StringHelper
    {
        public static string SqlIn(string[] array)
        {
            string[] tempArray = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                tempArray[i] = array[i];
                tempArray[i] = tempArray[i].Replace("'", "''");
                tempArray[i] = tempArray[i].Insert(0, "'");
                tempArray[i] = tempArray[i].Insert(tempArray[i].Length, "'");
            }
            return string.Join(",", tempArray);
        }
        public static string SqlIn(string str,char split=',')
        {
            string[] array= str.Split(split);
            return SqlIn( array);
        }
        public static string[] StrToArray(string str, char split = ',')
        {
            if (string.IsNullOrEmpty(str))
            {
                return new string[0];
            }
            return str.Split(split);
        }
        public static string ArrayToStr(string[] str, string split = ",")
        {

            return string.Join(split, str);
        }
    }
}

