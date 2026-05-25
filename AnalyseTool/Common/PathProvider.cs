using System.IO;
using System.Reflection;

namespace AnalyseTool.Common
{
    internal sealed class PathProvider
    {
        public static string RootDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedData.ToolData.PLUGIN_NAME);

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions — the default extensions root. Extensions live one
        /// level deeper, under a Revit-version folder (e.g. <c>extensions\2025\&lt;extension&gt;</c>), so the same
        /// machine can host builds for several Revit versions side by side.</summary>
        public static string ExtensionsRoot => Path.Combine(ProfilePath, "extensions");

        public static string DebugServerUrl => "http://localhost:22524";
    }
}
