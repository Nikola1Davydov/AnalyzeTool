namespace SharedData
{
    // internal ON PURPOSE: this shared-project file is compiled into SEVERAL assemblies
    // (App, Core, Tools, Launcher, Test). A public copy in each would make every cross-assembly
    // use of ToolData ambiguous (CS0433). Each assembly that needs it imports SharedData.projitems
    // and uses its own private copy.
    internal static class ToolData
    {
        public const string PLUGIN_AUTHOR = "Nikolai Davydov";
        public const string PLUGIN_NAME = "AnalyseTool";
        public const string PLUGIN_VERSION = "1.4.3.0";
        public const string LINK_TO_GITHUB = "https://github.com/Nikola1Davydov/AnalyzeTool";
        public const string LINK_TO_DOWNLOADS = "https://github.com/Nikola1Davydov/AnalyzeTool/releases";
        public const string LINK_TO_DONATE = ""; // TODO: Add link to donate
    }
}
