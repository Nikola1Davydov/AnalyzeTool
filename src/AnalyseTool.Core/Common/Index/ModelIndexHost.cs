using AnalyseTool.Core.Common.Dispatch;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Serilog;
using System.Collections.Concurrent;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>
    /// Where the index meets Revit's events: one <see cref="ModelIndexSession"/> per open, indexable
    /// model, the DocumentChanged handler that feeds its journal (copy the ids, wake the loop, return —
    /// nothing is read inside the handler), and the document lifecycle that starts and stops sessions.
    /// Subscribed once from the host bootstrap, in a valid API context, like DocumentTracker.
    /// </summary>
    internal static class ModelIndexHost
    {
        private static RevitTaskHub? _hub;
        private static readonly ConcurrentDictionary<string, ModelIndexSession> Sessions = new();
        private static volatile string? _activeKey;

        /// <summary>Raised on a session's loop thread whenever its status changes. The host forwards it
        /// to its windows; Core knows nothing about them.</summary>
        public static event Action<ModelIndexSession>? StatusChanged;

        public static bool IsInitialized => _hub is not null;

        public static void Initialize(UIApplication uiApp, RevitTaskHub hub)
        {
            if (_hub is not null) return;
            _hub = hub;
            SqliteRuntime.EnsureProvider();

            Application app = uiApp.Application;
            app.DocumentChanged += OnDocumentChanged;
            app.DocumentOpened += (_, e) => Track(e.Document, makeActive: false);
            app.DocumentClosing += (_, e) => Untrack(e.Document);
            app.DocumentSaved += (_, e) => Checkpoint(e.Document);
            app.DocumentSynchronizedWithCentral += (_, e) => Checkpoint(e.Document);
            // The active document decides which index the commands answer from; ViewActivated is the
            // reliable switch signal (see DocumentTracker).
            uiApp.ViewActivated += (_, e) => Track(e.Document, makeActive: true);

            if (uiApp.ActiveUIDocument?.Document is Document current) Track(current, makeActive: true);
        }

        /// <summary>The session of the active document, or null when none is indexable (a family, nothing open).</summary>
        public static ModelIndexSession? Active =>
            _activeKey is { } key && Sessions.TryGetValue(key, out ModelIndexSession? session) ? session : null;

        public static IReadOnlyCollection<ModelIndexSession> All => Sessions.Values.ToArray();

        // Revit thread.
        private static void Track(Document? doc, bool makeActive)
        {
            if (doc is null || !ModelIdentity.IsIndexable(doc)) return;
            string key;
            try { key = ModelIdentity.KeyOf(doc); }
            catch (Exception ex) { Log.Warning(ex, "Model index: no key for {Title}", doc.Title); return; }

            if (makeActive) _activeKey = key;
            if (Sessions.ContainsKey(key)) return;

            ModelIndexSession session = new(key, ModelIdentity.IndexPath(key), new HubSlots(_hub!, key));
            session.StatusChanged += s =>
            {
                try { StatusChanged?.Invoke(s); }
                catch (Exception ex) { Log.Warning(ex, "A model index status subscriber threw"); }
            };
            if (Sessions.TryAdd(key, session))
            {
                Log.Information("Model index: tracking {Title} as {Key}", doc.Title, key);
                session.Start();
            }
            else
            {
                session.Dispose();
            }
        }

        private static void Untrack(Document? doc)
        {
            if (doc is null || !ModelIdentity.IsIndexable(doc)) return;
            string key;
            try { key = ModelIdentity.KeyOf(doc); }
            catch (Exception) { return; }
            if (Sessions.TryRemove(key, out ModelIndexSession? session))
            {
                Log.Information("Model index: closing {Key}", key);
                session.Stop();
            }
            if (_activeKey == key) _activeKey = null;
        }

        private static void Checkpoint(Document? doc)
        {
            if (doc is null || !ModelIdentity.IsIndexable(doc)) return;
            try
            {
                if (Sessions.TryGetValue(ModelIdentity.KeyOf(doc), out ModelIndexSession? session))
                    session.RequestCheckpoint();
            }
            catch (Exception) { /* a document on its way out */ }
        }

        // Revit thread, at the end of every transaction: the cost here is a copy of the id lists and a
        // semaphore release, whether one element changed or ten thousand. Reading happens later, in the
        // session's own pass (#118, layer 0).
        private static void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            Document? doc;
            try { doc = e.GetDocument(); }
            catch (Exception) { return; }
            if (doc is null || !ModelIdentity.IsIndexable(doc)) return;

            string key;
            try { key = ModelIdentity.KeyOf(doc); }
            catch (Exception) { return; }
            if (!Sessions.TryGetValue(key, out ModelIndexSession? session)) return;

            session.Journal.Record(new ChangeBatch(
                Ids(e.GetAddedElementIds()),
                Ids(e.GetModifiedElementIds()),
                Ids(e.GetDeletedElementIds()),
                e.GetTransactionNames().ToList(),
                DateTime.UtcNow));
            session.Signal();
        }

        private static List<long> Ids(ICollection<ElementId> ids)
        {
            List<long> list = new(ids.Count);
            foreach (ElementId id in ids) list.Add(id.Value);
            return list;
        }

        /// <summary>Production slots: a hub work item that finds the session's document among the open
        /// ones by model key — never a stored Document reference, which a close would invalidate.</summary>
        private sealed class HubSlots : IRevitSlots
        {
            private readonly RevitTaskHub _hub;
            private readonly string _key;

            public HubSlots(RevitTaskHub hub, string key)
            {
                _hub = hub;
                _key = key;
            }

            public Task<T> RunAsync<T>(Func<Document, T> work, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return _hub.EnqueueAsync(app =>
                {
                    Document? doc = Find(app);
                    if (doc is null) throw new DocumentGoneException(_key);
                    return work(doc);
                });
            }

            private Document? Find(UIApplication app)
            {
                foreach (Document doc in app.Application.Documents)
                {
                    if (!ModelIdentity.IsIndexable(doc)) continue;
                    try
                    {
                        if (ModelIdentity.KeyOf(doc) == _key) return doc;
                    }
                    catch (Exception) { /* a document on its way out */ }
                }
                return null;
            }
        }
    }
}
