using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalyseTool.ServiceDefaults.Serialization;

/// <summary>
///     Provides the source-generated JSON serialization metadata of the application.
/// </summary>
/// <remarks>
///     Add frequently serialized types here for faster serialization.
///     A type absent from this context still works through the reflection resolver registered after it.
/// </remarks>
[JsonSerializable(typeof(JsonElement))]
public sealed partial class ApplicationSerializerContext : JsonSerializerContext;
