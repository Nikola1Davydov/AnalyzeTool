using AnalyseTool.Common.Extensions;
using AnalyseTool.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace AnalyseTool.Features.Scripting
{
    /// <summary>
    /// Compiles a C# snippet with Roslyn and runs it once (ephemeral) — the AI's "scratchpad" for Revit
    /// (cf. send_code_to_revit). The snippet is either a bare body (uiapp/uidoc/doc in scope) or a full
    /// IRevitTask; it runs through the SAME dispatch context, so RunInRevitAsync / transactions work.
    ///
    /// GATED: refuses to run unless C# execution is explicitly enabled in Settings (off by default), and
    /// hidden from the MCP tool list while disabled (see <see cref="Common.Transport.McpBridgeServer"/>).
    /// Promote a working snippet into a permanent command with SaveAsCommand.
    /// </summary>
    [RevitCommand(
        Description = "Compiles and runs a C# snippet inside Revit and returns its result. The snippet is " +
                      "either a bare statement body (with uiapp/uidoc/doc in scope, may 'return' any object) " +
                      "or a full IRevitTask class. Disabled by default — must be enabled in AnalyseTool Settings.",
        InputType = typeof(Request),
        Destructive = true)] // arbitrary code: assume it can modify the model
    internal sealed class ExecuteRevitCode : IRevitTask
    {
        /// <summary>Wire/command name — referenced by the MCP bridge to gate this tool's visibility.</summary>
        public const string CommandName = nameof(ExecuteRevitCode);

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            if (!CodeExecutionSettings.Enabled)
                throw new InvalidOperationException(
                    "C# code execution is disabled. Enable it in AnalyseTool Settings to run snippets.");

            Request? request = ctx.Payload.As<Request>();
            if (request is null || string.IsNullOrWhiteSpace(request.Code))
                throw new InvalidOperationException("No code provided.");

            string assemblyName = "adhoc_" + Guid.NewGuid().ToString("N");
            ScriptCompileResult compiled = RoslynScriptCompiler.CompileSnippet(request.Code, assemblyName, request.Description);
            if (!compiled.Success)
                return new { error = "Compilation failed.", diagnostics = compiled.Errors };

            ExtensionLoadContext alc = new(assemblyName); // collectible, in-memory, no private deps
            try
            {
                Assembly assembly = alc.LoadImage(compiled.Assembly!, compiled.Pdb);

                Type? taskType = assembly.GetTypes().FirstOrDefault(t =>
                    typeof(IRevitTask).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (taskType is null)
                    return new { error = "Compiled code contains no IRevitTask." };

                IRevitTask task = (IRevitTask)Activator.CreateInstance(taskType)!;
                object? result = await task.ExecuteAsync(ctx, ct);

                // Detach the result from the snippet's (anonymous) types into a host-owned JToken BEFORE
                // unloading the context — otherwise serialization could touch unloaded metadata.
                return result is null ? JValue.CreateNull() : JToken.FromObject(result);
            }
            finally
            {
                try { alc.Unload(); } catch { /* references may linger; GC collects later */ }
            }
        }

        internal sealed class Request
        {
            /// <summary>The C# to compile and run — a bare body or a full IRevitTask class.</summary>
            public string Code { get; set; } = string.Empty;

            /// <summary>Optional description used when a bare body is wrapped into a command.</summary>
            public string? Description { get; set; }
        }
    }
}
