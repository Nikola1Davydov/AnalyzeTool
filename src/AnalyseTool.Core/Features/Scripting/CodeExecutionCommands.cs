using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Core.Features.Scripting
{
    /// <summary>Reads the current C# code-execution on/off state for the Settings page.</summary>
    [RevitCommand(
        Description = "Returns whether ad-hoc C# code execution is enabled.",
        ReadOnly = true,
        HiddenFromMcp = true)] // local safety setting, not an AI tool
    internal sealed class GetCodeExecutionStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(new
            {
                enabled = CodeExecutionSettings.Enabled,
                // True when an organization policy locks the switch: render it read-only.
                managed = CodeExecutionSettings.IsManaged,
                origin = CodeExecutionSettings.Origin,
            });
    }

    /// <summary>Toggles C# code execution from the Settings page.</summary>
    [RevitCommand(
        Description = "Enables or disables ad-hoc C# code execution.",
        InputType = typeof(Request),
        HiddenFromMcp = true)] // the AI must not be able to grant itself code execution
    internal sealed class SetCodeExecution : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request? request = ctx.Payload.As<Request>();
            if (request is null)
                throw new InvalidOperationException("Payload is missing.");

            CodeExecutionSettings.SetEnabled(request.Enabled); // throws when the policy locks it
            return Task.FromResult<object?>(new
            {
                enabled = CodeExecutionSettings.Enabled,
                managed = CodeExecutionSettings.IsManaged,
                origin = CodeExecutionSettings.Origin,
            });
        }

        internal sealed class Request
        {
            [Description("True to allow C# execution commands to run, false to refuse them.")]
            public bool Enabled { get; set; }
        }
    }
}
