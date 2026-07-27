using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AnalyseTool.Fragments.Revit
{
    /// <summary>
    /// A standalone Revit command that runs the exporter and writes a .frag to the desktop, so the
    /// mapper can be tried on a real model without touching the plugin.
    /// <para>
    /// Read-only: no transaction is opened and nothing in the document changes. It reports the
    /// numbers that actually decide the next step — how long tessellation took, how big the result
    /// is, and how much geometry was shared rather than duplicated.
    /// </para>
    /// <para>
    /// Prototype scaffolding. The real integration is a command in <c>Tools/</c> driven by the
    /// CommandQueue; this exists to answer "does it work on my model" without that work first.
    /// </para>
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportFragmentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDocument = commandData?.Application?.ActiveUIDocument;
            Document? document = uiDocument?.Document;
            if (document is null)
            {
                message = "Open a project first.";
                return Result.Failed;
            }

            bool activeViewOnly = AskScope(document);

            try
            {
                RevitExportOptions options = new()
                {
                    ViewId = activeViewOnly ? document.ActiveView?.Id : null,
                    IncludeAllParameters = true,
                };

                Stopwatch stopwatch = Stopwatch.StartNew();
                FragmentModel model = new RevitFragmentExporter(options).Export(document);
                long tessellateMs = stopwatch.ElapsedMilliseconds;

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"{Sanitise(document.Title)}{(activeViewOnly ? "-view" : "")}.frag");

                stopwatch.Restart();
                using (FileStream file = File.Create(path))
                    FragmentWriter.Write(model, file);
                long writeMs = stopwatch.ElapsedMilliseconds;

                Report(model, path, tessellateMs, writeMs, activeViewOnly);
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                message = exception.ToString();
                return Result.Failed;
            }
        }

        private static bool AskScope(Document document)
        {
            TaskDialog dialog = new("Export .frag")
            {
                MainInstruction = "What should be exported?",
                MainContent = $"Document: {document.Title}",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Active view only",
                "Much faster — start here to see whether the result is right.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "The whole model",
                "Everything the document contains. This is the number that matters for performance.");

            return dialog.Show() != TaskDialogResult.CommandLink2;
        }

        private static void Report(
            FragmentModel model, string path, long tessellateMs, long writeMs, bool activeViewOnly)
        {
            int samples = model.Items.Sum(item => item.Samples.Count);
            int profiles = model.Shells.Sum(shell => shell.Profiles.Count);
            long bytes = new FileInfo(path).Length;

            // samples > shells means geometry is being shared across instances, which is the whole
            // reason to write this format from Revit rather than through IFC.
            double reuse = model.Shells.Count == 0 ? 0 : (double)samples / model.Shells.Count;

            TaskDialog dialog = new("Export .frag")
            {
                MainInstruction = "Done",
                MainContent =
                    $"Scope: {(activeViewOnly ? "active view" : "whole model")}\n" +
                    $"Items: {model.Items.Count:N0}\n" +
                    $"Shells (unique geometries): {model.Shells.Count:N0}\n" +
                    $"Samples (placements): {samples:N0}   →  {reuse:F1}x reuse\n" +
                    $"Triangles: {profiles:N0}\n" +
                    $"Materials: {model.Materials.Count:N0}\n\n" +
                    $"Tessellation: {tessellateMs:N0} ms\n" +
                    $"Serialisation: {writeMs:N0} ms\n" +
                    $"File: {bytes:N0} bytes\n\n{path}",
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
        }

        private static string Sanitise(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(name) ? "model" : name;
        }
    }
}
