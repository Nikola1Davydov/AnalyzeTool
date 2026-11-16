namespace AnalyseTool.Sdk
{
    /// <summary>A progress update reported by a long-running command.</summary>
    /// <param name="Fraction">Completion in the range 0..1.</param>
    /// <param name="Message">Optional short status text (e.g. "Deleting types…").</param>
    public sealed record ProgressInfo(double Fraction, string? Message = null);

    /// <summary>
    /// Opt-in capability for a command that wants to report progress. The host injects a
    /// <see cref="Progress"/> sink (bound to the calling window) before <c>ExecuteAsync</c> runs; the
    /// command calls <c>Progress?.Report(...)</c> from its work loop. Commands that don't need progress
    /// simply don't implement this — the base <see cref="IRevitTask"/> contract is unchanged.
    /// </summary>
    public interface IProgressAware
    {
        /// <summary>Sink for progress updates, set by the host. Null when the caller isn't listening.</summary>
        IProgress<ProgressInfo>? Progress { get; set; }
    }
}
