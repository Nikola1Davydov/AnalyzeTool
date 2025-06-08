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
            string plaginPath(string revitVersion) => Path.Combine(solutionPath.FullName, $@"AnalyseTool\bin\Release R{revitVersion}\publish\Revit 20{revitVersion} Release R{revitVersion} addin", @"*.*");

            string plaginPath23 = plaginPath("23");
            string plaginPath24 = plaginPath("24");
            string plaginPath25 = plaginPath("25");

            string targetPath23 = targetPath("23");
            string targetPath24 = targetPath("24");
            string targetPath25 = targetPath("25");

            string licencePath = Path.Combine(solutionPath.FullName, "LICENSE.rtf");

            Project project = new Project("AnalyzeTool",
                              new Dir(targetPath23,
                                  new Files(plaginPath23)),
                                    new Dir(targetPath24,
                                  new Files(plaginPath24)),
                                    new Dir(targetPath25,
                                  new Files(plaginPath25)));

            project.GUID = new Guid("e74a2dbc-8131-4240-abde-e2a776451bba");
            project.Version = new Version(1, 0, 1, 0);
            project.Scope = InstallScope.perUser;
            project.LicenceFile = licencePath;
            project.UI = WUI.WixUI_InstallDir;

            project.BackgroundImage = ""; // 493 x 312 pixels
            project.BannerImage = ""; // 493 x 58 pixels
            project.ControlPanelInfo = new ProductInfo
            {
                Manufacturer = "Nikolai Davydov",
                ProductIcon = Path.Combine(solutionPath.FullName, @"SharedData\Resources\Icons\AnalyzeTool_Icon.ico"),
                Name = "AnalyseTool",
                Readme = "https://github.com/Nikola1Davydov/AnalyzeTool",
                HelpLink = "https://github.com/Nikola1Davydov/AnalyzeTool",
                UrlInfoAbout = "https://github.com/Nikola1Davydov/AnalyzeTool",
                UrlUpdateInfo = "https://github.com/Nikola1Davydov/AnalyzeTool/releases"
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