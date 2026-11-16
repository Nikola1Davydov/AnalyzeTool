using System.Text.Json.Serialization;
using AnalyseTool.Services.Parameters;
using Microsoft.Extensions.Logging;

namespace AnalyseTool.Services.Actions;

/// <summary>How <see cref="ParameterWriteService" /> treats a parameter that already has a value.</summary>
public enum SetDataMode
{
    /// <summary>Always write.</summary>
    Overwrite,

    /// <summary>Skip a parameter that has a value.</summary>
    OnlyIfEmpty,

    /// <summary>Skip a parameter whose value equals the new one.</summary>
    SkipIfEqual
}

/// <summary>One write: which element, which parameter (<c>BuiltInParameter</c> value or <c>ParameterElement</c> id), what value.</summary>
/// <param name="ElementId">The element to modify.</param>
/// <param name="ParameterId">A negative <c>BuiltInParameter</c> value, or the <c>ParameterElement</c> id of a shared or project parameter.</param>
/// <param name="Value">The new value as text, parsed by the parameter's storage type.</param>
public sealed record ParameterWrite(long ElementId, long ParameterId, string Value);

/// <summary>
///     The outcome of a bulk parameter write. <see cref="Skipped" /> counts the items whose element or parameter was not
///     found, was read-only, could not take the value, or that the mode filtered out.
/// </summary>
/// <param name="Ok">Whether the transaction was committed.</param>
/// <param name="Written">How many values were written.</param>
/// <param name="Skipped">How many items were not written.</param>
/// <param name="Error">Why nothing was written, if so.</param>
/// <param name="Warnings">Revit warnings the transaction resolved.</param>
/// <param name="Problems">The items that failed, with the reason; absent when none failed.</param>
public sealed record SetDataResult(
    bool Ok,
    int Written,
    int Skipped,
    string? Error,
    IReadOnlyList<TransactionWarning> Warnings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<WriteProblem>? Problems = null);

/// <summary>One item of a parameter batch that was not written, and why.</summary>
/// <param name="ElementId">The element of the item.</param>
/// <param name="ParameterId">The parameter of the item.</param>
/// <param name="Reason">Why it was not written.</param>
public sealed record WriteProblem(long ElementId, long ParameterId, string Reason);

/// <summary>
///     The core of <c>SetDataToParameters</c> as a function of a <see cref="Document" />: one transaction over the given
///     items, per-item failures reported rather than thrown. Call it on the Revit thread.
/// </summary>
public sealed class ParameterWriteService(ILogger<ParameterWriteService>? logger = null)
{
    /// <summary>Writes the items in one transaction; one bad item never fails the batch.</summary>
    public SetDataResult Write(Document document, IReadOnlyList<ParameterWrite?> items, SetDataMode mode, string transactionName = "Set data to parameters")
    {
        using var transaction = new Transaction(document, transactionName);
        transaction.Start();
        var failures = CollectingFailuresPreprocessor.Apply(transaction, logger);

        var written = 0;
        var skipped = 0;
        var problems = new List<WriteProblem>();
        foreach (var item in items)
        {
            if (item is null)
            {
                skipped++;
                continue;
            }

            try
            {
                if (WriteOne(document, item, mode))
                {
                    written++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Per item, not per batch: one value that cannot be converted must not roll back the good writes
                skipped++;
                problems.Add(new WriteProblem(item.ElementId, item.ParameterId, exception.Message));
            }
        }

        var rolledBack = failures.RollbackReason(transaction.Commit());
        if (rolledBack is not null)
        {
            return new SetDataResult(false, 0, items.Count, rolledBack, failures.Warnings, problems.Count == 0 ? null : problems);
        }

        return new SetDataResult(true, written, skipped, null, failures.Warnings, problems.Count == 0 ? null : problems);
    }

    /// <summary>True when the value was written; false when the element or parameter was not found, is read-only, or the mode filtered it out.</summary>
    private static bool WriteOne(Document document, ParameterWrite item, SetDataMode mode)
    {
        var element = document.GetElement(new ElementId(item.ElementId));
        if (element is null)
        {
            return false;
        }

        var parameter = Resolve(document, element, item.ParameterId);
        if (parameter is null || parameter.IsReadOnly)
        {
            return false;
        }

        switch (mode)
        {
            case SetDataMode.OnlyIfEmpty when parameter.HasValue:
            case SetDataMode.SkipIfEqual when parameter.GetParameterValue() == item.Value:
                return false;
        }

        parameter.SetParameterValue(item.Value);
        return true;
    }

    /// <summary>A built-in parameter is looked up directly; anything else first among the element's parameters, then as a <c>ParameterElement</c>.</summary>
    private static Parameter? Resolve(Document document, Element element, long parameterId)
    {
        var id = new ElementId(parameterId);
        if (ParameterUtils.IsBuiltInParameter(id))
        {
            return element.get_Parameter((BuiltInParameter)parameterId);
        }

        foreach (Parameter parameter in element.Parameters)
        {
            if (parameter?.Id is not null && parameter.Id.Value == parameterId)
            {
                return parameter;
            }
        }

        var definition = (document.GetElement(id) as ParameterElement)?.GetDefinition();
        return definition is null ? null : element.get_Parameter(definition);
    }
}
