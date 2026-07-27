using System.ComponentModel;
using AnalyseTool.Fragments.Revit;
using AnalyseTool.Sdk;
using Autodesk.Revit.DB;

namespace AnalyseTool.Fragments.Ext
{
    /// <summary>
    /// Writes the open document as a .frag file. Registered as <c>analysetool.fragments.Export</c>,
    /// so it is reachable from the WebView UI via <c>AT.invoke(...)</c> and from an AI agent over
    /// MCP — the same command either way.
    /// <para>
    /// Read-only: it opens no transaction and changes nothing. Everything that touches the model
    /// happens inside <see cref="IRevitContext.RunInRevitAsync"/>.
    /// </para>
    /// <para>
    /// The returned stats are the ones worth acting on. <c>reuse</c> — placements divided by unique
    /// geometries — says whether family geometry is being shared across instances; at 1.0 the
    /// instancing is not working, and that is the first thing to fix.
    /// </para>
    /// </summary>
    [RevitCommand("Export",
        Description = "Exports the open Revit document as a That Open .frag file for viewing in the " +
                      "browser. Returns the output path plus stats: items, unique geometries, " +
                      "placements, triangles, timings and file size. Read-only. Pass activeViewOnly " +
                      "to export just what the current view shows, which is much faster.",
        ReadOnly = true,
        InputType = typeof(ExportFragments.Input))]
    public sealed class ExportFragments : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            Input? input = revitContext.Payload.As<Input>();

            return revitContext.RunInRevitAsync<object?>(app =>
            {
                Document document = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("Open a project first.");

                bool activeViewOnly = input?.ActiveViewOnly ?? false;
                RevitExportOptions options = new()
                {
                    ViewId = activeViewOnly ? document.ActiveView?.Id : null,
                    IncludeAllParameters = input?.IncludeAllParameters ?? true,
                };

                long startedAt = Environment.TickCount64;
                FragmentModel model = new RevitFragmentExporter(options).Export(document);
                long tessellateMs = Environment.TickCount64 - startedAt;

                string path = ResolvePath(input?.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                startedAt = Environment.TickCount64;
                using (FileStream file = File.Create(path))
                    FragmentWriter.Write(model, file);
                long writeMs = Environment.TickCount64 - startedAt;

                int samples = model.Items.Sum(item => item.Samples.Count);
                int triangles = model.Shells.Sum(shell => shell.Profiles.Count);

                return new
                {
                    path,
                    scope = activeViewOnly ? "active view" : "whole model",
                    items = model.Items.Count,
                    shells = model.Shells.Count,
                    samples,
                    // Above 1.0 means geometry is shared across instances instead of duplicated.
                    reuse = model.Shells.Count == 0 ? 0 : Math.Round((double)samples / model.Shells.Count, 2),
                    triangles,
                    materials = model.Materials.Count,
                    tessellateMs,
                    writeMs,
                    bytes = new FileInfo(path).Length,
                };
            });
        }

        /// <summary>
        /// Defaults to <c>model.frag</c> in this extension's own folder — the folder WebView2 serves
        /// the page from, so the UI reaches it with a plain relative fetch.
        /// <para>
        /// The path is rebuilt from the well-known extensions root rather than read off the assembly:
        /// the collectible ALC loads extensions from BYTES, so <c>Assembly.Location</c> is empty here.
        /// </para>
        /// </summary>
        private static string ResolvePath(string? requested)
        {
            if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested!);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AnalyseTool", "extensions", "AnalyseTool.Fragments", "model.frag");
        }

        public sealed record Input
        {
            [Description("Export only what the active view shows. Much faster; start here.")]
            public bool ActiveViewOnly { get; set; }

            [Description("Write every readable parameter as an attribute. Defaults to true.")]
            public bool? IncludeAllParameters { get; set; }

            [Description("Where to write the file. Defaults to model.frag inside this extension's " +
                         "own folder, which is where the viewer page looks for it.")]
            public string? Path { get; set; }
        }
    }
}
