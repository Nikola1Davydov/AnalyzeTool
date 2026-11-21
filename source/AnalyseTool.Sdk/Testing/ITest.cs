namespace AnalyseTool.Sdk.Testing
{
    /// <summary>What every test can say about itself. Call these last: they end the chain.</summary>
    public interface ITest
    {
        /// <summary>
        /// A stable id. Accepted failures (the baseline) are kept by id: without one the id follows the name, and renaming the
        /// test, or changing what a rule checks, starts its baseline afresh.
        /// </summary>
        ITest Id(string id);

        /// <summary>The name shown on the page.</summary>
        ITest Name(string name);

        /// <summary>How bad a failure is; <see cref="TestSeverity.Error"/> by default.</summary>
        ITest Severity(TestSeverity severity);

        /// <summary>A label for filtering.</summary>
        ITest Trait(string name, string value);

        /// <summary>A slow test (geometry, the whole model): runs only when asked for, not with every run.</summary>
        ITest Slow();

        /// <summary>Does not run; the page shows the reason.</summary>
        ITest Skip(string reason);
    }
}
