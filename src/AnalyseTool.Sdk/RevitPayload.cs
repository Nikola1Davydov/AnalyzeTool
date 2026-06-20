using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnalyseTool.Sdk
{
    /// <summary>The request payload passed to a command, wrapping the incoming JSON.</summary>
    public sealed class RevitPayload
    {
        private readonly JToken _token;

        internal RevitPayload(JToken? token)
        {
            _token = token ?? JValue.CreateNull();
        }

        /// <summary>Deserializes the payload into <typeparamref name="T"/> (case-insensitive). Returns
        /// the type's default when the payload is null/empty.</summary>
        public T? As<T>() => _token.ToObject<T>();

        /// <summary>The raw JSON of the payload, if you'd rather parse it yourself.</summary>
        public string RawJson => _token.ToString(Formatting.None);
    }
}
