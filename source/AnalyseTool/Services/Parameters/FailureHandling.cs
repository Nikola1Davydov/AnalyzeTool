using Microsoft.Extensions.Logging;

namespace AnalyseTool.Services.Parameters;

/// <summary>One Revit warning that a write transaction resolved on its own.</summary>
/// <param name="Description">Revit's own description text, as the user would have read it in the dialog.</param>
/// <param name="ElementIds">The elements Revit blamed; empty when the warning is document-wide.</param>
public sealed record TransactionWarning(string Description, IReadOnlyList<long> ElementIds);

/// <summary>
///     The failure preprocessor of every write transaction of the built-in commands. It deletes warnings, so Revit never
///     raises a modal dialog on the Revit thread that nobody watches, and records them first, so the result can carry them.
///     Errors roll the transaction back without a dialog; <see cref="RollbackReason" /> says why.
/// </summary>
public sealed partial class CollectingFailuresPreprocessor : IFailuresPreprocessor
{
    private readonly List<string> _errors = [];
    private readonly ILogger? _logger;
    private readonly List<TransactionWarning> _warnings = [];

    private CollectingFailuresPreprocessor(ILogger? logger)
    {
        _logger = logger;
    }

    /// <summary>Warnings resolved so far; Revit may call the preprocessor several times for one transaction.</summary>
    public IReadOnlyList<TransactionWarning> Warnings => _warnings;

    /// <summary>Descriptions of the errors that rolled the transaction back.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <inheritdoc />
    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var messages = failuresAccessor.GetFailureMessages();
        if (messages.Count > 0 && _logger is not null)
        {
            LogResolvedFailures(_logger, failuresAccessor.GetTransactionName(), messages.Count);
        }

        var hasErrors = false;
        foreach (var failure in messages)
        {
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                _warnings.Add(new TransactionWarning(
                    failure.GetDescriptionText(),
                    failure.GetFailingElementIds().Select(id => id.Value).ToList()));
                continue;
            }

            hasErrors = true;
            _errors.Add(failure.GetDescriptionText());
        }

        if (hasErrors)
        {
            return FailureProcessingResult.ProceedWithRollBack;
        }

        failuresAccessor.DeleteAllWarnings();
        return FailureProcessingResult.Continue;
    }

    /// <summary>Null when <paramref name="status" /> is committed; otherwise the sentence a result's error carries.</summary>
    public string? RollbackReason(TransactionStatus status)
    {
        if (status == TransactionStatus.Committed)
        {
            return null;
        }

        return _errors.Count == 0
            ? $"Revit did not commit the transaction ({status}); nothing was changed."
            : "Revit rolled the transaction back; nothing was changed. " + string.Join(" ", _errors.Distinct());
    }

    /// <summary>Attaches a collecting handler to a started transaction and returns it.</summary>
    public static CollectingFailuresPreprocessor Apply(Transaction transaction, ILogger? logger = null)
    {
        var preprocessor = new CollectingFailuresPreprocessor(logger);

        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(preprocessor);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);

        return preprocessor;
    }

    [LoggerMessage(LogLevel.Debug, "Resolved failures in transaction '{Transaction}': {Count} message(s)")]
    private static partial void LogResolvedFailures(ILogger logger, string transaction, int count);
}
