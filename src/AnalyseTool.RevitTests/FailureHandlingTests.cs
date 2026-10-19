using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using System.Reflection;

namespace AnalyseTool.RevitTests;

/// <summary>CollectingFailuresPreprocessor on a transaction that raises an ERROR: it must roll back
/// without handing the error to Revit's modal failure UI, and say why — the write services used to
/// report ok=true for a commit that never landed.</summary>
public sealed class FailureHandlingTests : SeededModel
{
    [Test]
    public async Task An_error_rolls_back_and_is_reported()
    {
        Wall wall = Walls[0];
        Parameter comments = wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

        using Transaction transaction = new(Document, "Write that fails");
        transaction.Start();
        CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);
        comments.Set("must not land");
        Document.PostFailure(new FailureMessage(AnyErrorDefinition()).SetFailingElement(wall.Id));
        TransactionStatus status = transaction.Commit();

        using (Assert.Multiple())
        {
            await Assert.That(status).IsNotEqualTo(TransactionStatus.Committed);
            await Assert.That(failures.Errors).IsNotEmpty();
            await Assert.That(failures.RollbackReason(status)).IsNotNull();
            await Assert.That(wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString() ?? string.Empty)
                .IsNotEqualTo("must not land");
        }
    }

    [Test]
    public async Task A_clean_commit_has_no_rollback_reason()
    {
        using Transaction transaction = new(Document, "Write that lands");
        transaction.Start();
        CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);
        Walls[0].get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("lands");
        TransactionStatus status = transaction.Commit();

        await Assert.That(failures.RollbackReason(status)).IsNull();
    }

    /// <summary>A built-in failure of Error severity. Found through the registry rather than named, so
    /// the test does not hang on one id surviving every Revit year.</summary>
    private static FailureDefinitionId AnyErrorDefinition()
    {
        FailureDefinitionRegistry registry = Autodesk.Revit.ApplicationServices.Application.GetFailureDefinitionRegistry();
        foreach (Type group in typeof(BuiltInFailures).GetNestedTypes())
            foreach (PropertyInfo property in group.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(FailureDefinitionId)) continue;
                FailureDefinitionId id = (FailureDefinitionId)property.GetValue(null)!;
                if (registry.FindFailureDefinition(id)?.GetSeverity() == FailureSeverity.Error) return id;
            }
        throw new InvalidOperationException("Revit registers no built-in failure of Error severity.");
    }
}
