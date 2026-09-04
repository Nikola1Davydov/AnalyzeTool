namespace AnalyseTool.Core.Common.Index
{
    /// <summary>What one DocumentChanged event said: the three id lists and the transaction names, copied
    /// out of the event on the Revit thread and nothing else — the handler's whole job (#118, layer 0).</summary>
    internal sealed record ChangeBatch(
        IReadOnlyList<long> Added,
        IReadOnlyList<long> Modified,
        IReadOnlyList<long> Deleted,
        IReadOnlyList<string> Transactions,
        DateTime Utc);

    /// <summary>The journal, folded: which elements to read again and which to mark deleted. An element
    /// touched five times is read once; one added and then deleted is not read at all.</summary>
    internal sealed record ChangeSet(
        IReadOnlyCollection<long> ToRead,
        IReadOnlyCollection<long> ToDelete,
        int Batches,
        IReadOnlyList<string> Transactions,
        bool Overflowed)
    {
        public bool IsEmpty => ToRead.Count == 0 && ToDelete.Count == 0 && !Overflowed;
    }

    /// <summary>
    /// The change journal of one document: a bounded, in-memory list of <see cref="ChangeBatch"/>es
    /// between two applications. Session-scoped and never the source of truth — when it overflows
    /// (a paste of half a model, a Reload Latest after a week away) it says so, and the indexer runs a
    /// reconcile sweep instead of replaying a list it cannot trust to be complete.
    /// </summary>
    internal sealed class ChangeJournal
    {
        /// <summary>Ids held before the journal gives up and asks for a reconcile. Ids are cheap (a long
        /// each), so this is generous; it bounds memory, not work — the work is bounded by the indexer's
        /// "huge batch" rule.</summary>
        public const int Capacity = 500_000;

        private readonly object _gate = new();
        private readonly List<ChangeBatch> _batches = new();
        private int _ids;
        private bool _overflowed;

        /// <summary>Called on the Revit thread from the DocumentChanged handler. Copies, counts, returns.</summary>
        public void Record(ChangeBatch batch)
        {
            lock (_gate)
            {
                int count = batch.Added.Count + batch.Modified.Count + batch.Deleted.Count;
                if (_ids + count > Capacity)
                {
                    _overflowed = true;
                    _batches.Clear();
                    _ids = 0;
                    return;
                }
                _batches.Add(batch);
                _ids += count;
            }
        }

        /// <summary>Ids waiting to be applied — the honest "pending" number on every status answer.</summary>
        public int PendingIds
        {
            get { lock (_gate) return _overflowed ? Capacity : _ids; }
        }

        public bool HasPending
        {
            get { lock (_gate) return _overflowed || _batches.Count > 0; }
        }

        /// <summary>Takes everything recorded so far, folded. The journal is empty afterwards; changes that
        /// arrive while the indexer applies this set land in the next one.</summary>
        public ChangeSet Drain()
        {
            List<ChangeBatch> taken;
            bool overflowed;
            lock (_gate)
            {
                taken = new List<ChangeBatch>(_batches);
                overflowed = _overflowed;
                _batches.Clear();
                _ids = 0;
                _overflowed = false;
            }
            return Coalesce(taken, overflowed);
        }

        /// <summary>The fold, as a pure function (tier-1 testable). Order matters: a later batch decides
        /// the fate of an id — deleted after modified is deleted; added after deleted (an undo, or Revit
        /// reusing the id) is read.</summary>
        public static ChangeSet Coalesce(IReadOnlyList<ChangeBatch> batches, bool overflowed)
        {
            HashSet<long> toRead = new();
            HashSet<long> toDelete = new();
            List<string> transactions = new();

            foreach (ChangeBatch batch in batches)
            {
                foreach (long id in batch.Added) { toDelete.Remove(id); toRead.Add(id); }
                foreach (long id in batch.Modified) { toDelete.Remove(id); toRead.Add(id); }
                foreach (long id in batch.Deleted) { toRead.Remove(id); toDelete.Add(id); }
                foreach (string name in batch.Transactions)
                    if (!string.IsNullOrWhiteSpace(name) && !transactions.Contains(name)) transactions.Add(name);
            }

            return new ChangeSet(toRead, toDelete, batches.Count, transactions, overflowed);
        }
    }
}
