using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Threading;
using AnalyseTool.Sdk;
using Autodesk.Revit.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProgressInfo = AnalyseTool.Hosting.Commands.ProgressInfo;
using SdkProgressInfo = AnalyseTool.Sdk.ProgressInfo;

namespace AnalyseTool.Extensions.Loading;

/// <summary>
///     Turns the <c>IRevitTask</c> classes of an extension assembly into pipeline commands: the public Sdk contract of 1.x on
///     the pipeline of 2.0. Every call creates a new instance, as 1.x did.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>The name is <c>&lt;extension id&gt;.&lt;name&gt;</c>: the name from <c>[RevitCommand]</c>, otherwise the class name.</item>
///         <item>The payload reaches the command as <c>RevitPayload</c> over Newtonsoft, and the result goes back through Newtonsoft:
///         an extension of 1.x sends and receives the same JSON as before.</item>
///         <item>AI clients see a command unless it says <c>HiddenFromMcp</c>; its input schema comes from <c>InputType</c>.</item>
///     </list>
/// </remarks>
internal static class SdkCommands
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    /// <summary>Describes the commands of an extension assembly.</summary>
    /// <param name="assembly">The extension assembly.</param>
    /// <param name="extensionId">The id of the extension: the prefix of the names.</param>
    /// <param name="problems">Receives the types that could not be described.</param>
    public static IReadOnlyList<CommandDescriptor> Describe(Assembly assembly, string extensionId, ICollection<string> problems)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            foreach (var reason in exception.LoaderExceptions.OfType<Exception>().Select(error => error.Message).Distinct())
            {
                problems.Add($"A type could not be loaded: {reason}");
            }

            types = exception.Types.OfType<Type>().ToArray();
        }

        var commands = new List<CommandDescriptor>();
        foreach (var type in types.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IRevitTask).IsAssignableFrom(type)))
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add($"{type.FullName} needs a public parameterless constructor: the host creates it for every call.");
                continue;
            }

            var attribute = type.GetCustomAttribute<RevitCommandAttribute>();
            var name = $"{extensionId}.{(string.IsNullOrWhiteSpace(attribute?.Name) ? type.Name : attribute!.Name)}";
            var annotations = new CommandAnnotations(
                ExposeToMcp: !(attribute?.HiddenFromMcp ?? false),
                ReadOnly: attribute?.ReadOnly ?? false,
                Destructive: attribute?.Destructive ?? false,
                InputSchema: Schema(attribute?.InputType, problems) ?? EmptyObjectSchema,
                OutputSchema: Schema(attribute?.OutputType, problems));

            // RequestType JsonElement: the transports pass the arguments as they came, the command reads them itself
            commands.Add(new CommandDescriptor(name, type, typeof(JsonElement), typeof(JsonElement), attribute?.Description, CreateInvoker(type))
            {
                Annotations = annotations
            });
        }

        return commands;
    }

    private static CommandInvoker CreateInvoker(Type type)
    {
        return async (services, context, _) =>
        {
            var task = (IRevitTask)Activator.CreateInstance(type)!;
            if (task is IProgressAware aware)
            {
                aware.Progress = new ProgressAdapter(context.Progress);
            }

            var payload = context.RawPayload.ValueKind is JsonValueKind.Undefined ? JValue.CreateNull() : JToken.Parse(context.RawPayload.GetRawText());
            var revitContext = new RevitContext(new RevitPayload(payload), services.GetRequiredService<IRevitDispatcher>(), context.CancellationToken);
            var result = await task.ExecuteAsync(revitContext, context.CancellationToken).ConfigureAwait(false);
            return ToJson(result);
        };
    }

    /// <summary>The result as Newtonsoft writes it — what a 1.x page received — handed to the pipeline as JSON.</summary>
    internal static JsonElement? ToJson(object? result)
    {
        if (result is null)
        {
            return null;
        }

        var json = result is JToken token ? token.ToString(Formatting.None) : JsonConvert.SerializeObject(result);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement? Schema(Type? type, ICollection<string> problems)
    {
        if (type is null)
        {
            return null;
        }

        try
        {
            var schema = JsonNode.Parse(AIJsonUtilities.CreateJsonSchema(type).GetRawText())!;
            RelaxNullableRequired(schema);
            return System.Text.Json.JsonSerializer.SerializeToElement(schema);
        }
        catch (Exception exception)
        {
            problems.Add($"No JSON schema for {type.FullName}: {exception.Message}");
            return null;
        }
    }

    /// <summary>A nullable property is optional for a caller: «required» stays only for what cannot be null.</summary>
    private static void RelaxNullableRequired(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["required"] is JsonArray required && obj["properties"] is JsonObject properties)
                {
                    for (var index = required.Count - 1; index >= 0; index--)
                    {
                        if (required[index]?.GetValue<string>() is { } name && properties[name] is JsonObject property
                                                                             && property["type"] is JsonArray kinds
                                                                             && kinds.Any(kind => kind?.GetValue<string>() == "null"))
                        {
                            required.RemoveAt(index);
                        }
                    }

                    if (required.Count == 0)
                    {
                        obj.Remove("required");
                    }
                }

                foreach (var (_, child) in obj.ToList())
                {
                    if (child is not null)
                    {
                        RelaxNullableRequired(child);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array.OfType<JsonNode>())
                {
                    RelaxNullableRequired(child);
                }

                break;
        }
    }

    private sealed class ProgressAdapter(IProgress<ProgressInfo> target) : IProgress<SdkProgressInfo>
    {
        public void Report(SdkProgressInfo value)
        {
            target.Report(new ProgressInfo(Math.Clamp(value.Fraction, 0, 1), value.Message));
        }
    }

    /// <summary>The <c>IRevitContext</c> of a call: the payload and the way into the Revit thread.</summary>
    private sealed class RevitContext(RevitPayload payload, IRevitDispatcher dispatcher, CancellationToken cancellationToken) : IRevitContext
    {
        public RevitPayload Payload { get; } = payload;

        public Task<T> RunInRevitAsync<T>(Func<UIApplication, T> work)
        {
            return dispatcher.InvokeAsync(work, cancellationToken);
        }

        public Task RunInRevitAsync(Action<UIApplication> work)
        {
            return dispatcher.InvokeAsync(work, cancellationToken);
        }
    }
}
