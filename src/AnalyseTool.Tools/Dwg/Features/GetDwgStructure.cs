using AnalyseTool.Sdk;
using System.ComponentModel;

namespace AnalyseTool.Tools.Dwg
{
    /// <summary>
    /// Reads a DWG/DXF from disk and reports what is in it — WITHOUT importing, linking or opening it
    /// in Revit. The file is parsed by the out-of-process reader (see <see cref="DwgSidecarClient"/>),
    /// so nothing enters the document and nothing can slow it down.
    ///
    /// This is the call that comes first: it turns "should I import this 40 MB survey drawing?" into a
    /// list of layers with counts, which is a question someone can actually answer.
    /// </summary>
    [RevitCommand(
        Description = "Reads a .dwg or .dxf file from disk and returns its structure: layers with " +
                      "per-type entity counts, blocks with how often they are placed, drawing units, " +
                      "version and extents. The file is NOT imported or linked — Revit never opens it, " +
                      "so the document is untouched. Use this before ImportDwgAsCurves to pick the " +
                      "layers worth importing. Read-only. Cost: one full parse of the file, seconds " +
                      "for a normal drawing and up to minutes for a very large one.",
        ReadOnly = true,
        InputType = typeof(GetDwgStructure.Request),
        OutputType = typeof(DwgStructure))]
    internal sealed class GetDwgStructure : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();
            if (string.IsNullOrWhiteSpace(req.Path))
                throw new ArgumentException("path is required — give the full path of a .dwg or .dxf file.");

            // Deliberately outside RunInRevitAsync: parsing a drawing is slow I/O, and the Revit thread
            // is the single path to the model for every transport. Blocking it here would freeze the
            // whole tool for the duration of the parse.
            return await new DwgSidecarClient()
                .GetStructureAsync(req.Path, req.Space, req.Failsafe, ct)
                .ConfigureAwait(false);
        }

        public sealed class Request
        {
            [Description("Full path of the .dwg or .dxf file to inspect.")]
            public string Path { get; set; } = string.Empty;

            [Description("Which part of the drawing to report: 'model' (default), 'paper' or 'all'. " +
                         "'all' also counts the contents of block definitions, so its numbers are " +
                         "higher than what is actually drawn.")]
            public string? Space { get; set; }

            [Description("Error-tolerant parsing: collect diagnostics and keep going instead of failing " +
                         "on the first unreadable object. Use it for a file that was rejected with a " +
                         "read_failed error; the result's notifications say what was skipped.")]
            public bool Failsafe { get; set; }
        }
    }
}
