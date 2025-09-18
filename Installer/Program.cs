using System;
using System.IO;
using System.Linq;
using WixSharp;

namespace Installer
{
    public class Program
    {
        private static void Main()
        {
            System.IO.DirectoryInfo solutionPath = TryGetSolutionDirectoryInfo();
            string targetPath(string revitVersion) => Path.Combine("%CommonAppDataFolder%", $@"Autodesk\Revit\Addins\20{revitVersion}");
            string plaginPath(string revitVersion)
            {
                int revitVersionInt = int.Parse(revitVersion);
                if (revitVersionInt < 25)
                {
                    return Path.Combine(solutionPath.FullName, $@"AnalyseTool\bin\Release R{revitVersion}\net48", @"*.*");
                }
                {
                    return Path.Combine(solutionPath.FullName, $@"AnalyseTool\bin\Release R{revitVersion}\net8.0-windows", @"*.*");
                }
            }

            string plaginPath24 = plaginPath("24");
            string plaginPath25 = plaginPath("25");
            string plaginPath26 = plaginPath("26");

            string targetPath24 = targetPath("24");
            string targetPath25 = targetPath("25");
            string targetPath26 = targetPath("26");

            string licencePath = Path.Combine(solutionPath.FullName, "LICENSE.rtf");

            Project project = new Project(SharedData.ToolData.PLUGIN_NAME,
                                new Dir(targetPath24,
                                  new Files(plaginPath24)),
                                 new Dir(targetPath25,
                                  new Files(plaginPath25)),
                                    new Dir(targetPath26,
                                  new Files(plaginPath26))
                                    );

            project.GUID = new Guid("e74a2dbc-8131-4240-abde-e2a776451bba");
            project.Version = new Version(SharedData.ToolData.PLUGIN_VERSION);
            project.LicenceFile = licencePath;
            project.UI = WUI.WixUI_InstallDir;

            project.BackgroundImage = ""; // 493 x 312 pixels
            project.BannerImage = ""; // 493 x 58 pixels
            project.ControlPanelInfo = new ProductInfo
            {
                Manufacturer = SharedData.ToolData.PLUGIN_AUTHOR,
                ProductIcon = Path.Combine(solutionPath.FullName, "SharedData/Resources/Icons/AnalyzeTool_Icon.ico"),
                Name = SharedData.ToolData.PLUGIN_NAME,
                Readme = SharedData.ToolData.LINK_TO_GITHUB,
                HelpLink = SharedData.ToolData.LINK_TO_GITHUB,
                UrlInfoAbout = SharedData.ToolData.LINK_TO_GITHUB,
                UrlUpdateInfo = SharedData.ToolData.LINK_TO_DOWNLOADS
            };

            project.BuildMsi();
        }
        public static System.IO.DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
        {
            System.IO.DirectoryInfo directory = new System.IO.DirectoryInfo(
                currentPath ?? System.IO.Directory.GetCurrentDirectory());
            while (directory != null && !directory.GetFiles("*.sln").Any())
            {
                directory = directory.Parent;
            }
            return directory;
        }
    }
}