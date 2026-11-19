namespace AnalyseTool.Hosting.Scripting;

/// <summary>
///     The <c>Scripting</c> configuration section.
/// </summary>
[PublicAPI]
public sealed class ScriptingOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Scripting";

    /// <summary>
    ///     Gets or sets whether AI clients and pages may run any C# (<c>ExecuteCode</c>). By default on in the
    ///     <c>Development</c> environment and off otherwise.
    /// </summary>
    public bool? Enabled { get; set; }
}
