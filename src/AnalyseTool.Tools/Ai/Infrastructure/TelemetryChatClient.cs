using AnalyseTool.Sdk;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>
    /// Wraps any chat client and reports one <c>ai</c> telemetry event per call — provider, model,
    /// duration, outcome and token counts when the backend reports them (design §10). Never the prompt,
    /// never the answer. Reaches the sink through <see cref="HostTelemetry"/>, which is a no-op unless
    /// the organization policy lists <c>ai</c> among its events.
    /// </summary>
    internal sealed class TelemetryChatClient : DelegatingChatClient
    {
        private readonly string _providerId;
        private readonly string _model;

        public TelemetryChatClient(IChatClient inner, string providerId, string model) : base(inner)
        {
            _providerId = providerId;
            _model = model;
        }

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken);
                Report(started, "ok", response.Usage);
                return response;
            }
            catch (OperationCanceledException) { Report(started, "cancelled", null); throw; }
            catch (Exception ex) { Report(started, "error:" + ex.GetType().Name, null); throw; }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            long started = Stopwatch.GetTimestamp();
            string outcome = "ok";
            // yield may not sit inside a try with a catch, so the enumerator is stepped by hand.
            await using IAsyncEnumerator<ChatResponseUpdate> e = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool more;
                    try { more = await e.MoveNextAsync(); }
                    catch (OperationCanceledException) { outcome = "cancelled"; throw; }
                    catch (Exception ex) { outcome = "error:" + ex.GetType().Name; throw; }
                    if (!more) break;
                    yield return e.Current;
                }
            }
            finally
            {
                Report(started, outcome, null);
            }
        }

        private void Report(long startedTicks, string outcome, UsageDetails? usage)
        {
            HostTelemetry.Emit("ai", new Dictionary<string, object?>
            {
                ["provider"] = _providerId,
                ["model"] = _model,
                ["durationMs"] = (long)Stopwatch.GetElapsedTime(startedTicks).TotalMilliseconds,
                ["outcome"] = outcome,
                ["inputTokens"] = usage?.InputTokenCount,
                ["outputTokens"] = usage?.OutputTokenCount,
            });
        }
    }
}
