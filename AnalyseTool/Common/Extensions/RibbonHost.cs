using AnalyseTool.Common.Bootstrap;
using AnalyseTool.Common.Utils;
using Autodesk.Revit.UI;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdWin = Autodesk.Windows;

namespace AnalyseTool.Common.Extensions
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
        // extension id -> (button, key of the panel it currently sits in)
        private static readonly Dictionary<string, (AdWin.RibbonButton Button, string PanelKey)> _extButtons =
            new(StringComparer.OrdinalIgnoreCase);
        // "tab\npanel" -> the AdWindows panel source we created for it
        private static readonly Dictionary<string, AdWin.RibbonPanelSource> _adwPanels =
            new(StringComparer.Ordinal);
        // titles of custom tabs WE created via AdWindows (so cleanup never touches the Revit-made tab)
        private static readonly HashSet<string> _createdAdwTabs = new(StringComparer.Ordinal);
        // current descriptor per extension id — looked up at click time so manifest changes
        // (devUrl, entryHtml, …) take effect after Reload without recreating the button.
        private static readonly Dictionary<string, ExtensionDescriptor> _descriptors =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Build(UIControlledApplication app, string launcherPath)
        {
            RibbonEventHub.Initialize();

            string hostRevit = CommonUtils.HostRevitTag(app.ControlledApplication.VersionNumber);

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
        /// removes gone, updates changed (text/icon/tooltip) and moves buttons whose ui.tab/ui.panel
        /// changed — all live. Safe to call repeatedly (Build + Reload).</summary>
        public static void RefreshExtensionButtons(string hostRevit)
        {
            if (AdWin.ComponentManager.Ribbon is null) return; // ribbon not ready yet

            List<ExtensionDescriptor> found = ExtensionCatalog
                .Scan(PathProvider.ExtensionsDirectory, hostRevit)
                .Where(d => d.HasUi)
                .ToList();

            HashSet<string> foundIds = new(found.Select(d => d.Manifest.Id), StringComparer.OrdinalIgnoreCase);

            // Remove buttons whose extension is gone.
            foreach (string id in _extButtons.Keys.ToList())
            {
                if (foundIds.Contains(id)) continue;
                (AdWin.RibbonButton button, string panelKey) = _extButtons[id];
                if (_adwPanels.TryGetValue(panelKey, out AdWin.RibbonPanelSource? oldPanel))
                    oldPanel.Items.Remove(button);
                _extButtons.Remove(id);
                _descriptors.Remove(id);
            }

            // Add new / update / move existing.
            foreach (ExtensionDescriptor descriptor in found)
            {
                _descriptors[descriptor.Manifest.Id] = descriptor; // refresh for click-time lookup

                ExtensionUi ui = descriptor.Manifest.Ui!;
                ExtensionButton info = ui.Button!;
                string tab = string.IsNullOrWhiteSpace(ui.Tab) ? DefaultTab : ui.Tab!;
                string panelName = string.IsNullOrWhiteSpace(ui.Panel) ? ExtensionsPanelTitle : ui.Panel!;
                string panelKey = tab + "\n" + panelName;

                AdWin.RibbonPanelSource? source = GetOrCreateAdwPanel(tab, panelName, panelKey);
                if (source is null) continue;

                ImageSource? icon = LoadIcon(descriptor, info.Icon);

                if (_extButtons.TryGetValue(descriptor.Manifest.Id, out (AdWin.RibbonButton Button, string PanelKey) entry))
                {
                    // Move to a different panel/tab if it changed.
                    if (!string.Equals(entry.PanelKey, panelKey, StringComparison.Ordinal))
                    {
                        if (_adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? from))
                            from.Items.Remove(entry.Button);
                        source.Items.Add(entry.Button);
                        _extButtons[descriptor.Manifest.Id] = (entry.Button, panelKey);
                    }

                    entry.Button.Text = info.Text;
                    entry.Button.ToolTip = info.Tooltip;
                    if (icon != null) { entry.Button.Image = icon; entry.Button.LargeImage = icon; }
                    continue;
                }

                string id = descriptor.Manifest.Id;
                AdWin.RibbonButton button = new()
                {
                    Id = $"AnalyseTool.Ext.{id}",
                    Text = info.Text,
                    ShowText = true,
                    ShowImage = true,
                    Size = AdWin.RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    ToolTip = info.Tooltip,
                    CommandHandler = new RelayCommand(() =>
                        RibbonEventHub.Run(uiApp => OpenExtension(id, uiApp))),
                };
                if (icon != null) { button.Image = icon; button.LargeImage = icon; }

                source.Items.Add(button);
                _extButtons[descriptor.Manifest.Id] = (button, panelKey);
            }

            RemoveEmptyPanelsAndTabs();
        }

        /// <summary>Tears down AdWindows panels we created that are now empty, and any custom tab we
        /// created that ends up with no panels. The Revit-made "AnalyseTool" tab and its static
        /// panels are never touched (they aren't in our tracking sets).</summary>
        private static void RemoveEmptyPanelsAndTabs()
        {
            AdWin.RibbonControl? ribbon = AdWin.ComponentManager.Ribbon;
            if (ribbon is null) return;

            foreach (KeyValuePair<string, AdWin.RibbonPanelSource> entry in _adwPanels.ToList())
            {
                if (entry.Value.Items.Count > 0) continue;

                string tabTitle = entry.Key.Split('\n')[0];
                AdWin.RibbonTab? tab = ribbon.Tabs.FirstOrDefault(t => string.Equals(t.Title, tabTitle, StringComparison.Ordinal));
                AdWin.RibbonPanel? panel = tab?.Panels.FirstOrDefault(p => ReferenceEquals(p.Source, entry.Value));
                if (tab != null && panel != null) tab.Panels.Remove(panel);

                _adwPanels.Remove(entry.Key);
            }

            foreach (string tabTitle in _createdAdwTabs.ToList())
            {
                AdWin.RibbonTab? tab = ribbon.Tabs.FirstOrDefault(t => string.Equals(t.Title, tabTitle, StringComparison.Ordinal));
                if (tab is null || tab.Panels.Count > 0) continue;

                ribbon.Tabs.Remove(tab);
                _createdAdwTabs.Remove(tabTitle);
            }
        }

        public static void OpenSettings(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            new SettingsWindow().Show();
        }

        public static void Reload(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            AnalyseToolBootstrap.ReloadExtensions();                                  // C# command DLLs
            RefreshExtensionButtons(CommonUtils.HostRevitTag(uiApp.Application.VersionNumber));   // ribbon buttons

            TaskDialog.Show("AnalyseTool — Reload", "Extensions reloaded.");
        }

        private static void OpenExtension(string id, UIApplication uiApp)
        {
            if (!_descriptors.TryGetValue(id, out ExtensionDescriptor? descriptor)) return;

            AnalyseToolBootstrap.Initialize(uiApp);
            new ExtensionWindow(descriptor).Show();
        }

        /// <summary>Finds or creates the AdWindows panel for (tab, panel), creating a custom tab too
        /// if the manifest asks for one that doesn't exist yet (the official API can't do this at
        /// runtime, AdWindows can).</summary>
        private static AdWin.RibbonPanelSource? GetOrCreateAdwPanel(string tab, string panel, string key)
        {
            if (_adwPanels.TryGetValue(key, out AdWin.RibbonPanelSource? cached)) return cached;

            AdWin.RibbonControl? ribbon = AdWin.ComponentManager.Ribbon;
            if (ribbon is null) return null;

            AdWin.RibbonTab adwTab = FindOrCreateTab(ribbon, tab);

            AdWin.RibbonPanelSource source = new()
            {
                Title = panel,
                Id = "AnalyseTool.ExtPanel." + key,
            };
            adwTab.Panels.Add(new AdWin.RibbonPanel { Source = source });
            _adwPanels[key] = source;
            return source;
        }

        private static AdWin.RibbonTab FindOrCreateTab(AdWin.RibbonControl ribbon, string title)
        {
            foreach (AdWin.RibbonTab candidate in ribbon.Tabs)
            {
                if (string.Equals(candidate.Title, title, StringComparison.Ordinal))
                    return candidate;
            }

            AdWin.RibbonTab tab = new()
            {
                Title = title,
                Id = "AnalyseTool.ExtTab." + title,
                IsVisible = true,
                IsEnabled = true,
            };
            ribbon.Tabs.Add(tab);
            _createdAdwTabs.Add(title); // only tabs we created are eligible for cleanup
            return tab;
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

        /// <summary>Loads the button icon from the file named in the manifest (which must sit next to
        /// plugin.json). Falls back to a generated default icon when the path is missing or invalid.</summary>
        private static ImageSource LoadIcon(ExtensionDescriptor descriptor, string? icon)
        {
            if (!string.IsNullOrWhiteSpace(icon))
            {
                try
                {
                    string path = Path.Combine(descriptor.Directory, icon!); // icon lives beside plugin.json
                    if (File.Exists(path)) return new BitmapImage(new Uri(path));
                }
                catch { /* fall through to the default */ }
            }

            string label = string.IsNullOrWhiteSpace(descriptor.Manifest.DisplayName)
                ? descriptor.Manifest.Id
                : descriptor.Manifest.DisplayName;
            return BuildDefaultIcon(label);
        }

        /// <summary>Draws a default icon (colored rounded square + the extension's initial) so a button
        /// always has an image, with no dependency on packaged resources or files.</summary>
        private static ImageSource BuildDefaultIcon(string label)
        {
            const int size = 32;
            string letter = string.IsNullOrWhiteSpace(label) ? "?" : label.Trim().Substring(0, 1).ToUpperInvariant();

            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), null,
                    new Rect(0, 0, size, size), 6, 6);

                FormattedText text = new(letter, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 18, Brushes.White, 1.0);
                dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            }

            RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
