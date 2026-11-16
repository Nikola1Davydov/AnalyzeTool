using Autodesk.Revit.UI;

namespace AnalyseTool.Infrastructure.Ribbon;

/// <summary>
///     Enables a button only when a document is open.
/// </summary>
/// <remarks>Revit calls availability classes very often, so the check stays trivial.</remarks>
[PublicAPI]
public sealed class DocumentAvailability : IExternalCommandAvailability
{
    /// <inheritdoc />
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return applicationData.ActiveUIDocument is not null;
    }
}

/// <summary>
///     Enables a button only when a project (not a family) document is open.
/// </summary>
/// <remarks>Revit calls availability classes very often, so the check stays trivial.</remarks>
[PublicAPI]
public sealed class ProjectAvailability : IExternalCommandAvailability
{
    /// <inheritdoc />
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return applicationData.ActiveUIDocument?.Document is { IsFamilyDocument: false };
    }
}
