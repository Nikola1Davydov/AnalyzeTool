using System.IO;
using System.Reflection;

namespace AnalyseTool.Common
{
    internal sealed class PathProvider
    {
        public static string RootDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static string ProfilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedData.ToolData.PLUGIN_NAME);

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\extensions — one sub-folder per user-authored extension.</summary>
        public static string ExtensionsDirectory => Path.Combine(ProfilePath, "extensions");

        public static string DebugServerUrl => "http://localhost:22524";
    }
}
