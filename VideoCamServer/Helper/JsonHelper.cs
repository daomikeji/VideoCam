using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace VideoCamServer.Helper
{
    public class JsonHelper
    {
        /// <summary>
        /// 对象转化为json
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="t"></param>
        /// <returns></returns>
        public static string ObjectToJson<T>(T t)
        {
          
           return Newtonsoft.Json.JsonConvert.SerializeObject(t, new Newtonsoft.Json.JsonSerializerSettings()
           {
               NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore, 
                
           });
        }
        /// <summary>
        /// json转化为对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T JsonToObject<T>(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }
     /// <summary>
     /// json转化为xml
     /// </summary>
     /// <param name="json"></param>
     /// <returns></returns>
        public static XDocument JsonToXDocument(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeXNode(json);

        }
        /// <summary>
        /// 合并对象
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        public static object Marger(params object[] list)
        {
            JObject obj = new JObject();
            foreach(object o in list)
            {
                obj.Merge(JToken.FromObject(o));

            }
            return obj;

        }
        /// <summary>
        /// json找到属性值
        /// </summary>
        /// <param name="json"></param>
        /// <param name="property"></param>
        /// <returns></returns>
        public static string JsonFindProperty(string json,string property)
        {
            XDocument xDoument = JsonHelper.JsonToXDocument(json);
            XElement xElement = xDoument.Element(property);
            if (xElement == null)
            {
                return "";
            }
            return xElement.Value;

        }

    }
}

