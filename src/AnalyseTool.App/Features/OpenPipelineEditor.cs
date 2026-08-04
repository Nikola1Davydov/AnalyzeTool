using AnalyseTool.App.Common.Extensions;
using AnalyseTool.Sdk;
using System.ComponentModel;
using System.Windows;

namespace AnalyseTool.App.Features
{
    /// <summary>
    /// Opens the pipeline editor window, optionally on a saved pipeline.
    ///
    /// <para>A command rather than a ribbon button: the editor is reached FROM the runner ("edit this
    /// one"), which is where a person already is when they want to change a pipeline. A WebView cannot
    /// open a WPF window itself, so the panel asks the host — the same reason
    /// <see cref="PickFolder"/> exists.</para>
    ///
    /// <para>Single instance, like the other host windows: a second call focuses the open editor instead
    /// of stacking a duplicate over it. Hidden from MCP — an agent that wants to author a pipeline has
    /// <c>SavePipeline</c>, and opening windows on someone's screen is not an agent's business.</para>
    /// </summary>
    [RevitCommand(
        Description = "Opens the pipeline editor window. Payload: { name } to edit a saved pipeline, " +
                      "or nothing for a blank one.",
        InputType = typeof(OpenPipelineEditor.Request),
        HiddenFromMcp = true)]
    internal sealed class OpenPipelineEditor : IRevitTask
    {
        private static Window? _window;

        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            // On the Revit UI thread: a WPF window must be created and shown on the thread that owns the
            // rest of the UI, and RunInRevitAsync is how anything gets there.
            return ctx.RunInRevitAsync<object?>(_ =>
            {
                if (_window is not null)
                {
                    if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    return new { opened = true, focused = true };
                }

                Window window = new PipelineEditorWindow(req.Name);
                window.Closed += (_, _) => _window = null;
                _window = window;
                window.Show();
                return new { opened = true, focused = false };
            });
        }

        internal sealed class Request
        {
            [Description("Name of a saved pipeline to open, or empty for a new one.")]
            public string? Name { get; set; }
        }
    }
}
