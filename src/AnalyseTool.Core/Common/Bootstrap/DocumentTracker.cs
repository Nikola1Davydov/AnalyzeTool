using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Serilog;

namespace AnalyseTool.Core.Common.Bootstrap
{
    /// <summary>
    /// Detects when the ACTIVE document changes and raises one event for the host to fan out (e.g. so
    /// the always-alive dockable pane can reload). The document itself is never stored — consumers keep
    /// resolving it at the point of use via RunInRevitAsync; this only says "ask again".
    ///
    /// ViewActivated is the reliable signal for document switches (DocumentOpened fires before the new
    /// document is active, and switching between already-open documents only raises ViewActivated), but
    /// it also fires on every view change WITHIN a document — hence the key comparison.
    /// </summary>
    internal static class DocumentTracker
    {
        /// <summary>Raised on the Revit UI thread with the new document's title, or null when the last
        /// document was closed.</summary>
        public static event Action<string?>? DocumentChanged;

        private static string? _lastDocKey;
        private static bool _initialized;

        /// <summary>Subscribe once, from a valid Revit API context (Bootstrap.Initialize).</summary>
        public static void Initialize(UIApplication uiApp)
        {
            if (_initialized) return;
            _initialized = true;

            _lastDocKey = KeyOf(uiApp.ActiveUIDocument?.Document);

            uiApp.ViewActivated += (_, e) => OnMaybeChanged(e.Document);
            // DocumentClosed carries no document; the new active one (or none) is on the UIApplication.
            uiApp.Application.DocumentClosed += (_, _) => OnMaybeChanged(uiApp.ActiveUIDocument?.Document);
        }

        private static void OnMaybeChanged(Document? doc)
        {
            string? key = KeyOf(doc);
            if (key == _lastDocKey) return; // same document (e.g. a view switch inside it)
            _lastDocKey = key;

            string? title = doc?.Title;
            Log.Information("Active document changed: {Title}", title ?? "<none>");
            DocumentChanged?.Invoke(title);
        }

        // PathName alone is empty for unsaved documents, Title alone can collide — use both.
        private static string? KeyOf(Document? doc) =>
            doc is null ? null : doc.PathName + "|" + doc.Title;
    }
}
