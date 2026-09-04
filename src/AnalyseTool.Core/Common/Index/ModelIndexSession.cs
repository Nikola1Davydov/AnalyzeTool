using AnalyseTool.Core.Common.Dispatch;
using Autodesk.Revit.DB;
using Serilog;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>A short piece of work on the Revit thread against the session's document. In production
    /// this is a <see cref="RevitTaskHub"/> slot that resolves the document by model key; inside the
    /// Revit test engine it is a direct call, which is what lets the whole session run under tier 3.</summary>
    internal interface IRevitSlots
    {
        Task<T> RunAsync<T>(Func<Document, T> work, CancellationToken ct);
    }

    /// <summary>The session's document is no longer open: whatever was in flight stops.</summary>
    internal sealed class DocumentGoneException : Exception
    {
        public DocumentGoneException(string key) : base($"The document with model key {key} is no longer open.") { }
    }

    /// <summary>The knobs of the indexer, with the defaults the plan argues for. Tests set the pauses to
    /// zero so the whole pass stays on the calling (Revit) thread.</summary>
    internal sealed record IndexOptions(
        int ReadChunk = 150,
        int SweepChunk = 2000,
        int ChunkPauseMs = 25,
        int DebounceMs = 1500,
        int HugeBatch = 5000,
        double HugeFraction = 0.25);

    internal enum IndexState { Absent, Building, Reconciling, Applying, Ready, Error, Closed }

    /// <summary>
    /// The index of one open model, kept in step with it. One background loop per session, woken by
    /// the change journal, a rebuild request or a save; every pass ends with the index READY or says
    /// why not. Revit is touched only through <see cref="IRevitSlots"/>, in short chunks, and never
    /// while it is busy with the person (<see cref="RevitAvailability"/>).
    ///
    /// Three ways to catch up, chosen by what happened:
    /// <list type="bullet">
    /// <item><b>build</b> — no index yet, or a rebuild was asked for: read everything, in chunks;</item>
    /// <item><b>apply</b> — the journal has a batch: tombstone the deleted ids, re-read the rest;</item>
    /// <item><b>reconcile</b> — the document was (re)opened, the journal overflowed, or a batch is huge
    /// (a Reload Latest, a paste of a wing): sweep (id, VersionGuid) of the whole model — cheap, no
    /// parameters read — and re-read only what differs from the index; what the model no longer has
    /// becomes a tombstone. The same code brings a copy, or a model edited without the plugin, in line.</item>
    /// </list>
    /// A change recorded while a pass runs is simply the next pass. The status is honest at every
    /// moment: state, how far along, and how many ids still wait in the journal.
    /// </summary>
    internal sealed class ModelIndexSession : IDisposable
    {
        public string Key { get; }
        public string DbPath { get; }
        public ChangeJournal Journal { get; } = new();

        private readonly IRevitSlots _slots;
        private readonly IndexOptions _options;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly CancellationTokenSource _cts = new();
        private readonly object _gate = new();

        private ModelIndexStore? _store;
        private Task? _loop;
        private volatile IndexState _state = IndexState.Absent;
        private int _done, _total;
        private long _liveElements;
        private string? _message, _lastSyncUtc, _builtAtUtc, _title;
        private bool _wantFull, _wantReconcile, _wantCheckpoint;

        /// <summary>Raised on the session's loop thread after every state change; subscribers hop to their
        /// own thread. Exceptions from subscribers are logged and swallowed.</summary>
        public event Action<ModelIndexSession>? StatusChanged;

        public ModelIndexSession(string key, string dbPath, IRevitSlots slots, IndexOptions? options = null)
        {
            Key = key;
            DbPath = dbPath;
            _slots = slots;
            _options = options ?? new IndexOptions();
        }

        public IndexState State => _state;
        public string? Title => _title;
        public long LiveElements => Interlocked.Read(ref _liveElements);

        public IndexFreshness Freshness
        {
            get
            {
                IndexState state = _state;
                bool inProgress = state is IndexState.Building or IndexState.Reconciling or IndexState.Applying;
                return new IndexFreshness(
                    state.ToString().ToLowerInvariant(),
                    inProgress ? Volatile.Read(ref _done) : null,
                    inProgress ? Volatile.Read(ref _total) : null,
                    Journal.PendingIds,
                    _lastSyncUtc, _builtAtUtc, _message);
            }
        }

        /// <summary>Starts the loop and asks for the first pass (open the file; build or reconcile).</summary>
        public void Start()
        {
            _loop = Task.Run(LoopAsync);
            Signal();
        }

        /// <summary>Wakes the loop: the journal has something, or a request was set. Idempotent.</summary>
        public void Signal()
        {
            try { _signal.Release(); }
            catch (SemaphoreFullException) { /* already awake */ }
        }

        public void RequestRebuild(bool full)
        {
            lock (_gate)
            {
                if (full) _wantFull = true; else _wantReconcile = true;
            }
            Signal();
        }

        /// <summary>At the model's own save and sync moments: fold the WAL into the file.</summary>
        public void RequestCheckpoint()
        {
            lock (_gate) _wantCheckpoint = true;
            Signal();
        }

        /// <summary>True while a pass runs or one is due — what a caller waits on after RequestRebuild.</summary>
        public bool HasWork
        {
            get
            {
                if (_state is IndexState.Absent or IndexState.Building or IndexState.Reconciling or IndexState.Applying) return true;
                if (Journal.HasPending) return true;
                lock (_gate) return _wantFull || _wantReconcile;
            }
        }

        public async Task WaitUntilIdleAsync(CancellationToken ct)
        {
            while (HasWork && _state != IndexState.Error && _state != IndexState.Closed)
                await Task.Delay(300, ct);
        }

        /// <summary>Stops the loop; the store is closed on the loop's way out.</summary>
        public void Stop() => _cts.Cancel();

        public void Dispose()
        {
            Stop();
            _signal.Dispose();
        }

        /// <summary>One pass, inline on the caller's thread — the tier-3 entry point. With zero pauses in
        /// the options and direct slots, nothing here ever leaves the Revit thread.</summary>
        public Task RunOnceAsync(CancellationToken ct = default) => WorkAsync(ct);

        private async Task LoopAsync()
        {
            CancellationToken ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await _signal.WaitAsync(ct);
                    await DebounceAsync(ct);
                    try
                    {
                        await WorkAsync(ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (DocumentGoneException)
                    {
                        Set(IndexState.Closed);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Model index {Key}: pass failed", Key);
                        Set(IndexState.Error, message: $"{ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() — the document closed or Revit is shutting down.
            }
            finally
            {
                CloseStore();
            }
        }

        // Wait for the burst to end: a person's action is many DocumentChanged events in a row, and one
        // read after the last of them beats a read after each.
        private async Task DebounceAsync(CancellationToken ct)
        {
            if (_options.DebounceMs <= 0) return;
            while (true)
            {
                int before = Journal.PendingIds;
                await Task.Delay(_options.DebounceMs, ct);
                if (Journal.PendingIds == before) return;
            }
        }

        private async Task WorkAsync(CancellationToken ct)
        {
            if (_store is null)
            {
                _store = ModelIndexStore.Open(DbPath, out bool created);
                _builtAtUtc = _store.GetMeta("built_at");
                _lastSyncUtc = _store.GetMeta("last_sync_utc");
                _title = _store.GetMeta("title");
                _store.SetMeta("clean_close", "0");
                lock (_gate)
                {
                    // A fresh file is built; an existing one is reconciled on every open — the sweep is
                    // cheap, and it is what makes a copy, an unclean close or an edit without the plugin
                    // harmless instead of a lie.
                    if (created) _wantFull = true; else _wantReconcile = true;
                }
            }

            bool full, reconcile;
            lock (_gate)
            {
                full = _wantFull;
                reconcile = _wantReconcile;
                _wantFull = _wantReconcile = false;
            }

            if (full) await BuildAsync(ct);
            else if (reconcile) await ReconcileAsync(ct);

            while (Journal.HasPending)
            {
                ct.ThrowIfCancellationRequested();
                ChangeSet set = Journal.Drain();
                if (set.Overflowed || IsHuge(set))
                {
                    Log.Information("Model index {Key}: {Reads} reads / {Deletes} deletes in one batch — reconciling instead", Key, set.ToRead.Count, set.ToDelete.Count);
                    await ReconcileAsync(ct);
                    continue;
                }
                await ApplyAsync(set, ct);
            }

            bool checkpoint;
            lock (_gate) { checkpoint = _wantCheckpoint; _wantCheckpoint = false; }
            if (checkpoint) _store.Checkpoint();

            Interlocked.Exchange(ref _liveElements, _store.LiveElements);
            Set(IndexState.Ready);
        }

        private bool IsHuge(ChangeSet set)
        {
            long threshold = Math.Max(_options.HugeBatch, (long)(LiveElements * _options.HugeFraction));
            return set.ToRead.Count + set.ToDelete.Count > threshold;
        }

        private async Task BuildAsync(CancellationToken ct)
        {
            Set(IndexState.Building, 0, 0);
            _store!.Clear();

            (List<long> ids, string title, string path, string version) = await _slots.RunAsync(doc =>
                (ElementRowReader.CollectIds(doc).Select(id => id.Value).ToList(), doc.Title, doc.PathName, doc.Application.VersionNumber), ct);
            _title = title;
            _store.SetMeta("model_key", Key);
            _store.SetMeta("title", title);
            _store.SetMeta("path", path);
            _store.SetMeta("revit_version", version);

            Set(IndexState.Building, 0, ids.Count);
            await ReadAsync(ids, IndexState.Building, ct);

            string now = DateTime.UtcNow.ToString("O");
            _builtAtUtc = now;
            _lastSyncUtc = now;
            _store.SetMeta("built_at", now);
            _store.SetMeta("last_sync_utc", now);
            _store.Checkpoint();
            Log.Information("Model index {Key}: built, {Count} elements", Key, ids.Count);
        }

        private async Task ReconcileAsync(CancellationToken ct)
        {
            Set(IndexState.Reconciling, 0, 0);

            List<long> ids = await _slots.RunAsync(doc => ElementRowReader.CollectIds(doc).Select(id => id.Value).ToList(), ct);
            Dictionary<long, string> live = _store!.LiveVersions();
            HashSet<long> seen = new(ids.Count);
            List<long> toRead = new();

            Set(IndexState.Reconciling, 0, ids.Count);
            for (int offset = 0; offset < ids.Count; offset += _options.SweepChunk)
            {
                ct.ThrowIfCancellationRequested();
                await PauseWhileBusyAsync(ct);
                List<long> slice = ids.GetRange(offset, Math.Min(_options.SweepChunk, ids.Count - offset));
                List<(long Id, Guid Version)> sweep = await _slots.RunAsync(doc =>
                    ElementRowReader.SweepVersions(doc, slice.Select(id => new ElementId(id)).ToList()), ct);
                foreach ((long id, Guid version) in sweep)
                {
                    seen.Add(id);
                    if (!live.TryGetValue(id, out string? known) || !string.Equals(known, version.ToString(), StringComparison.OrdinalIgnoreCase))
                        toRead.Add(id);
                }
                Volatile.Write(ref _done, Math.Min(offset + slice.Count, ids.Count));
                await PauseBetweenChunksAsync(ct);
            }

            List<long> gone = live.Keys.Where(id => !seen.Contains(id)).ToList();
            _store.Tombstone(gone);

            Set(IndexState.Reconciling, 0, toRead.Count);
            await ReadAsync(toRead, IndexState.Reconciling, ct);

            string now = DateTime.UtcNow.ToString("O");
            _lastSyncUtc = now;
            _store.SetMeta("last_sync_utc", now);
            if (_builtAtUtc is null) { _builtAtUtc = now; _store.SetMeta("built_at", now); }
            Log.Information("Model index {Key}: reconciled — {Changed} re-read, {Gone} gone, {Total} swept", Key, toRead.Count, gone.Count, ids.Count);
        }

        private async Task ApplyAsync(ChangeSet set, CancellationToken ct)
        {
            Set(IndexState.Applying, 0, set.ToRead.Count);
            _store!.Tombstone(set.ToDelete);
            await ReadAsync(set.ToRead.ToList(), IndexState.Applying, ct);

            string now = DateTime.UtcNow.ToString("O");
            _lastSyncUtc = now;
            _store.SetMeta("last_sync_utc", now);
            Log.Debug("Model index {Key}: applied {Reads} reads, {Deletes} deletes ({Transactions})",
                Key, set.ToRead.Count, set.ToDelete.Count, string.Join(", ", set.Transactions));
        }

        /// <summary>Reads elements in chunks through the slots and writes each chunk off the Revit thread.
        /// An id the model no longer has becomes a tombstone; an element outside the index's scope is
        /// skipped; one Revit refuses to describe is logged and skipped — never fatal to the pass.</summary>
        private async Task ReadAsync(List<long> ids, IndexState state, CancellationToken ct)
        {
            ElementRowReader? reader = null;
            int done = 0;
            for (int offset = 0; offset < ids.Count; offset += _options.ReadChunk)
            {
                ct.ThrowIfCancellationRequested();
                await PauseWhileBusyAsync(ct);
                List<long> slice = ids.GetRange(offset, Math.Min(_options.ReadChunk, ids.Count - offset));

                (List<ElementRead> batch, List<long> missing) = await _slots.RunAsync(doc =>
                {
                    reader ??= new ElementRowReader(doc, withParameters: true);
                    List<ElementRead> read = new(slice.Count);
                    List<long> gone = new();
                    foreach (long id in slice)
                    {
                        Element? element = doc.GetElement(new ElementId(id));
                        if (element is null) { gone.Add(id); continue; }
                        if (!ElementRowReader.IsIndexed(element)) continue;
                        try
                        {
                            read.Add(reader.Read(element));
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("Model index: element {Id} ({Category}) could not be read — {Error}",
                                id, element.Category?.Name ?? "no category", ex.Message);
                        }
                    }
                    return (read, gone);
                }, ct);

                _store!.Write(batch);
                _store.Tombstone(missing);

                done += slice.Count;
                Set(state, done, ids.Count);
                await PauseBetweenChunksAsync(ct);
            }
        }

        // The hub only runs a slot when Revit is idle, so a busy Revit (a dialog, an edit mode) would
        // simply hold the slot — but holding it means the loop cannot notice a Stop; polling here does.
        private static async Task PauseWhileBusyAsync(CancellationToken ct)
        {
            while (RevitAvailability.IsRevitBusy)
                await Task.Delay(250, ct);
        }

        private Task PauseBetweenChunksAsync(CancellationToken ct) =>
            _options.ChunkPauseMs > 0 ? Task.Delay(_options.ChunkPauseMs, ct) : Task.CompletedTask;

        private void Set(IndexState state, int? done = null, int? total = null, string? message = null)
        {
            _state = state;
            if (done is not null) Volatile.Write(ref _done, done.Value);
            if (total is not null) Volatile.Write(ref _total, total.Value);
            _message = message;
            try { StatusChanged?.Invoke(this); }
            catch (Exception ex) { Log.Warning(ex, "A model index status subscriber threw"); }
        }

        private void CloseStore()
        {
            ModelIndexStore? store = _store;
            _store = null;
            if (store is null) return;
            try
            {
                store.SetMeta("clean_close", "1");
                store.Checkpoint();
            }
            catch (Exception ex) { Log.Warning(ex, "Model index {Key}: close-out failed", Key); }
            finally { store.Dispose(); }
        }
    }
}
