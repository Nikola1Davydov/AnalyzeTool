namespace AnalyseTool.Core.Common.Utils
{
    /// <summary>
    /// One value, fetched asynchronously and kept for a fixed time — for commands that ask a remote
    /// service a question whose answer changes rarely (the GitHub release feed allows 60 anonymous
    /// requests an hour, and every open window asks). Concurrent callers share one fetch. A null result
    /// means "no answer" and is NOT cached, so the next call retries instead of repeating a failure for
    /// the whole lifetime.
    /// </summary>
    internal sealed class AsyncTtlCache<T> where T : class
    {
        private sealed record Entry(T Value, DateTimeOffset ExpiresUtc);

        private readonly TimeSpan _ttl;
        private readonly TimeProvider _time;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Entry? _entry;

        public AsyncTtlCache(TimeSpan ttl, TimeProvider? time = null)
        {
            _ttl = ttl;
            _time = time ?? TimeProvider.System;
        }

        public async Task<T?> GetOrCreateAsync(Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
        {
            if (TryGetFresh(out T? cached)) return cached;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Whoever waited on the gate finds the value the previous holder fetched.
                if (TryGetFresh(out cached)) return cached;

                T? value = await factory(ct).ConfigureAwait(false);
                if (value is not null)
                    Volatile.Write(ref _entry, new Entry(value, _time.GetUtcNow() + _ttl));
                return value;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Drops the cached value; the next call fetches again.</summary>
        public void Invalidate() => Volatile.Write(ref _entry, null);

        private bool TryGetFresh(out T? value)
        {
            Entry? entry = Volatile.Read(ref _entry);
            if (entry is not null && _time.GetUtcNow() < entry.ExpiresUtc)
            {
                value = entry.Value;
                return true;
            }
            value = null;
            return false;
        }
    }
}
