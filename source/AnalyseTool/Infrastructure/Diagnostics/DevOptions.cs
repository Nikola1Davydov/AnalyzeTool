#if DEBUG
namespace AnalyseTool.Infrastructure.Diagnostics;

/// <summary>
///     Debug-only user setting to check <c>IUserSettings</c> (docs/manual-tests.md): the value must survive a Revit restart.
/// </summary>
public sealed class DevOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Dev";

    /// <summary>Gets or sets how many times the «User settings» button was clicked.</summary>
    public int ClickCount { get; set; }
}
#endif
