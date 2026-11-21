namespace AnalyseTool.Sdk.Testing
{
    /// <summary>How bad a failing test is: what the page sorts and colours by.</summary>
    public enum TestSeverity
    {
        /// <summary>Worth knowing, nothing to fix.</summary>
        Info,

        /// <summary>Should be fixed.</summary>
        Warning,

        /// <summary>Must be fixed — the default.</summary>
        Error
    }
}
