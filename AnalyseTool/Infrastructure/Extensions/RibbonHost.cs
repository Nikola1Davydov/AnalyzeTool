using AnalyseTool.Common;
using AnalyseTool.Infrastructure.Bootstrap;
using AnalyseTool.Utils;
using Autodesk.Revit.UI;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdWin = Autodesk.Windows;

namespace AnalyseTool.Infrastructure.Extensions
{
    /// <summary>
    /// Builds the Revit ribbon for AnalyseTool. Static buttons (main / Settings / Reload) use the
    /// official Revit API (stable). Per-extension buttons use the unofficial AdWindows API so they
    /// can be added, removed and updated live (Reload) without restarting Revit. Invoked from the
    /// Launcher's OnStartup via reflection, so all logic lives in the isolated AnalyseTool assembly.
    /// </summary>
    internal static class RibbonHost
    {
        private const string MainCommandClass = "AnalyseTool.Launcher.RevitCommands.AnalyseToolCommand";
        private const string SettingsCommandClass = "AnalyseTool.Launcher.RevitCommands.SettingsCommand";
        private const string ReloadCommandClass = "AnalyseTool.Launcher.RevitCommands.ReloadCommand";
        private const string DefaultTab = "AnalyseTool";
        private const string ExtensionsPanelTitle = "Extensions";

        private static readonly HashSet<string> _createdTabs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AdWin.RibbonButton> _extButtons =
            new(StringComparer.OrdinalIgnoreCase);
        private static AdWin.RibbonPanelSource? _extPanelSource;

        public static void Build(UIControlledApplication app, string launcherPath)
        {
            RibbonEventHub.Initialize();

            string hostRevit = HostRevitTag(app.ControlledApplication.VersionNumber);

            // Static buttons via the official API.
            RibbonPanel mainPanel = GetOrCreatePanel(app, DefaultTab, "Parameter");
            AddStaticButton(mainPanel, "AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME, launcherPath,
                MainCommandClass, "Open AnalyseTool", appIcon: "AnalyzeTool_Icon.png");

            RibbonPanel managePanel = GetOrCreatePanel(app, DefaultTab, "Manage");
            AddStaticButton(managePanel, "AnalyseToolSettings", "Settings", launcherPath,
                SettingsCommandClass, "Show where extensions live and how to add them", appIcon: null);
            AddStaticButton(managePanel, "AnalyseToolReload", "Reload", launcherPath,
                ReloadCommandClass, "Reload extensions (DLLs + buttons) without restarting Revit", appIcon: null);

            // Dynamic extension buttons via AdWindows.
            RefreshExtensionButtons(hostRevit);
        }

        /// <summary>Re-scans manifests and brings the AdWindows extension buttons in sync: adds new,
        /// removes gone, updates changed. Safe to call repeatedly (Build + Reload).</summary>
        public static void RefreshExtensionButtons(string hostRevit)
        {
            AdWin.RibbonPanelSource? source = EnsureExtensionPanel();
            if (source is null) return;

            List<ExtensionDescriptor> found = ExtensionCatalog
                .Scan(PathProvider.ExtensionsDirectory, hostRevit)
                .Where(d => d.HasUi)
                .ToList();

            HashSet<string> foundIds = new(found.Select(d => d.Manifest.Id), StringComparer.OrdinalIgnoreCase);

            // Remove buttons whose extension is gone.
            foreach (string id in _extButtons.Keys.ToList())
            {
                if (foundIds.Contains(id)) continue;
                source.Items.Remove(_extButtons[id]);
                _extButtons.Remove(id);
            }

            // Add new / update existing.
            foreach (ExtensionDescriptor descriptor in found)
            {
                ExtensionButton info = descriptor.Manifest.Ui!.Button!;
                ImageSource? icon = LoadIcon(descriptor, info.Icon);

                if (_extButtons.TryGetValue(descriptor.Manifest.Id, out AdWin.RibbonButton? existing))
                {
                    existing.Text = info.Text;
                    existing.ToolTip = info.Tooltip;
                    if (icon != null) { existing.Image = icon; existing.LargeImage = icon; }
                    continue;
                }

                ExtensionDescriptor captured = descriptor;
                AdWin.RibbonButton button = new()
                {
                    Id = $"AnalyseTool.Ext.{descriptor.Manifest.Id}",
                    Text = info.Text,
                    ShowText = true,
                    ShowImage = true,
                    Size = AdWin.RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    ToolTip = info.Tooltip,
                    CommandHandler = new RelayCommand(() =>
                        RibbonEventHub.Run(uiApp => OpenExtension(captured, uiApp))),
                };
                if (icon != null) { button.Image = icon; button.LargeImage = icon; }

                source.Items.Add(button);
                _extButtons[descriptor.Manifest.Id] = button;
            }
        }

        public static void OpenSettings(UIApplication uiApp)
        {
            string folder = PathProvider.ExtensionsDirectory;
            Directory.CreateDirectory(folder);

            TaskDialog dialog = new("AnalyseTool — Extensions")
            {
                MainInstruction = "User extensions",
                MainContent =
                    $"Drop one folder per extension into:\n{folder}\n\n" +
                    "Each folder needs a plugin.json (id, version, targetRevit, sdkVersion, " +
                    "optional entryAssembly for C# commands and optional ui{ entryHtml, button } for a ribbon button). " +
                    "Use Reload after changes.",
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open extensions folder");
            if (dialog.Show() == TaskDialogResult.CommandLink1)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }

        public static void Reload(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            AnalyseToolBootstrap.ReloadExtensions();                                  // C# command DLLs
            RefreshExtensionButtons(HostRevitTag(uiApp.Application.VersionNumber));   // ribbon buttons

            TaskDialog.Show("AnalyseTool — Reload", "Extensions reloaded.");
        }

        private static void OpenExtension(ExtensionDescriptor descriptor, UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            new ExtensionWindow(descriptor).Show();
        }

        private static AdWin.RibbonPanelSource? EnsureExtensionPanel()
        {
            if (_extPanelSource != null) return _extPanelSource;

            AdWin.RibbonControl? ribbon = AdWin.ComponentManager.Ribbon;
            if (ribbon is null) return null; // ribbon not ready yet (e.g. too early at startup)

            AdWin.RibbonTab? tab = null;
            foreach (AdWin.RibbonTab candidate in ribbon.Tabs)
            {
                if (string.Equals(candidate.Title, DefaultTab, StringComparison.Ordinal))
                {
                    tab = candidate;
                    break;
                }
            }
            if (tab is null) return null;

            AdWin.RibbonPanelSource source = new() { Title = ExtensionsPanelTitle, Id = "AnalyseTool.Ext.Panel" };
            tab.Panels.Add(new AdWin.RibbonPanel { Source = source });
            _extPanelSource = source;
            return source;
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tab, string panel)
        {
            EnsureTab(app, tab);

            foreach (RibbonPanel existing in app.GetRibbonPanels(tab))
            {
                if (string.Equals(existing.Name, panel, StringComparison.Ordinal))
                    return existing;
            }
            return app.CreateRibbonPanel(tab, panel);
        }

        private static void EnsureTab(UIControlledApplication app, string tab)
        {
            if (!_createdTabs.Add(tab)) return;

            try { app.CreateRibbonTab(tab); }
            catch { /* tab already exists */ }
        }

        private static void AddStaticButton(RibbonPanel panel, string name, string text, string assemblyPath,
            string className, string? tooltip, string? appIcon)
        {
            PushButtonData data = new(name, text, assemblyPath, className);
            if (!string.IsNullOrWhiteSpace(tooltip)) data.ToolTip = tooltip;

            if (panel.AddItem(data) is not PushButton pushButton) return;

            try
            {
                if (!string.IsNullOrWhiteSpace(appIcon))
                {
                    BitmapImage image = new(new Uri($"pack://application:,,,/AnalyseTool;component/Resources/Icons/{appIcon}"));
                    pushButton.Image = image;
                    pushButton.LargeImage = image;
                }
            }
            catch { /* icon is best-effort */ }
        }

        private static ImageSource? LoadIcon(ExtensionDescriptor descriptor, string? icon)
        {
            if (string.IsNullOrWhiteSpace(icon)) return null;
            try
            {
                string path = Path.Combine(descriptor.Directory, icon!);
                return File.Exists(path) ? new BitmapImage(new Uri(path)) : null;
            }
            catch { return null; }
        }

        private static string HostRevitTag(string versionNumber) =>
            versionNumber.Length >= 4 ? $"R{versionNumber.Substring(2)}" : versionNumber;
    }
}
