using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Sdk
{
    public sealed class RevitPayload
    {
        private readonly JToken _token;

        internal RevitPayload(JToken? token)
        {
            _token = token ?? JValue.CreateNull();
        }

        public T? As<T>() => _token.ToObject<T>();

        public string RawJson => _token.ToString(Formatting.None);
    }
}
