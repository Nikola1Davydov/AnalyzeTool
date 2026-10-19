namespace AnalyseTool.Tests;

/// <summary>[NotInParallel] keys for tests that share process-wide state.</summary>
internal static class TestKeys
{
    /// <summary>The profile override, extension state and extension diagnostics: one test at a time.</summary>
    public const string ExtensionState = "extension-state";
}
