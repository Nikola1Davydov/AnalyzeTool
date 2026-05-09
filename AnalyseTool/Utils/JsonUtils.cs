using AnalyseTool.Common.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Utils
{
    internal class JsonUtils
    {
        internal static string BuildResponce<T>(string command, T dataModels)
        {
            return JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Payload = JToken.FromObject(dataModels)
            });
        }
        internal static string BuildResponce(string command, string dataModels)
        {
            return JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Payload = new JValue(dataModels)
            });
        }
        internal static string BuildResponce(string command, JObject dataModels)
        {
            return JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Payload = dataModels
            });
        }
        internal static string BuildResponce<T>(string command, IEnumerable<T> dataModels)
        {
            return JsonConvert.SerializeObject(new WebViewMessage()
            {
                Type = WebMessageTypeEnum.Response.ToString(),
                Command = command,
                Payload = JArray.FromObject(dataModels)
            });
        }
    }
}
