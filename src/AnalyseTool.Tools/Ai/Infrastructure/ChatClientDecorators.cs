using Microsoft.Extensions.AI;
using Serilog;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>
    /// Puts a deadline on every call: the caller's token is linked, not replaced, so pressing Cancel
    /// still stops the HTTP call AND an endpoint that never answers still hits the deadline. Both end in
    /// an <see cref="OperationCanceledException"/>; a caller tells them apart by asking whether ITS token
    /// was the one cancelled — the AI commands rely on exactly that.
    /// </summary>
    internal sealed class TimeoutChatClient : DelegatingChatClient
    {
        private readonly TimeSpan _timeout;

        public TimeoutChatClient(IChatClient inner, TimeSpan timeout) : base(inner) => _timeout = timeout;

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            return await base.GetResponseAsync(messages, options, cts.Token).ConfigureAwait(false);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);
            await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cts.Token)
                               .ConfigureAwait(false))
                yield return update;
        }
    }

    /// <summary>
    /// One log line per AI call: ok (elapsed, answer length), cancelled by the caller, timed out,
    /// unauthorized, or failed. Must sit OUTSIDE <see cref="TimeoutChatClient"/>: it sees the caller's
    /// own token, and that is what tells a user who changed their mind from an endpoint that never
    /// answered — calling both a timeout would make the log lie.
    /// </summary>
    internal sealed class SerilogChatClient : DelegatingChatClient
    {
        private readonly string _provider;
        private readonly string _model;
        private readonly ILogger? _logger;

        /// <param name="logger">Null = the global <see cref="Log.Logger"/>, read per call.</param>
        public SerilogChatClient(IChatClient inner, string provider, string model, ILogger? logger = null) : base(inner)
        {
            _provider = provider;
            _model = model;
            _logger = logger;
        }

        private ILogger Logger => _logger ?? Log.Logger;

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
                LogOk(started, response.Text.Length);
                return response;
            }
            catch (Exception ex)
            {
                LogFailure(ex, started, cancellationToken);
                throw;
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            long chars = 0;
            // Enumerated by hand: C# allows no `yield return` inside a try that has a catch, and the
            // failure has to be seen where MoveNextAsync throws it.
            IAsyncEnumerator<ChatResponseUpdate> enumerator =
                base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    ChatResponseUpdate update;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                        update = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        LogFailure(ex, started, cancellationToken);
                        throw;
                    }
                    chars += update.Text?.Length ?? 0;
                    yield return update;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            LogOk(started, chars);
        }

        private void LogOk(long started, long chars) =>
            Logger.Information("AI call ok: {Provider}/{Model}, {Elapsed} ms, {Chars} chars",
                _provider, _model, ElapsedMs(started), chars);

        private void LogFailure(Exception ex, long started, CancellationToken callerToken)
        {
            switch (ex)
            {
                case HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }:
                    Logger.Warning("AI call unauthorized ({Provider}/{Model})", _provider, _model);
                    break;
                case OperationCanceledException when callerToken.IsCancellationRequested:
                    Logger.Information("AI call cancelled by the caller after {Elapsed} ms ({Provider}/{Model})",
                        ElapsedMs(started), _provider, _model);
                    break;
                case OperationCanceledException:
                    Logger.Warning("AI call timed out after {Elapsed} ms ({Provider}/{Model})",
                        ElapsedMs(started), _provider, _model);
                    break;
                default:
                    Logger.Error(ex, "AI call failed ({Provider}/{Model})", _provider, _model);
                    break;
            }
        }

        private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
