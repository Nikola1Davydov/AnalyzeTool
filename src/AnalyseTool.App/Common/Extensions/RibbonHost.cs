using AnalyseTool.App.Common.Bootstrap;
using AnalyseTool.App.Common.Docking;
using AnalyseTool.Core.Common.Extensions;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AdWin = Autodesk.Windows;

namespace AnalyseTool.App.Common.Extensions
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
        private const string FamilyControlCommandClass = "AnalyseTool.Launcher.RevitCommands.FamilyControlCommand";
        private const string FamilyPaletteCommandClass = "AnalyseTool.Launcher.RevitCommands.FamilyPaletteCommand";
        private const string SettingsCommandClass = "AnalyseTool.Launcher.RevitCommands.SettingsCommand";
        private const string ReloadCommandClass = "AnalyseTool.Launcher.RevitCommands.ReloadCommand";
        private const string BugsCommandClass = "AnalyseTool.Launcher.RevitCommands.BugsCommand";
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
        // Open windows, so a second click focuses the existing one instead of stacking duplicates:
        // one Family Manager window, and one window per extension id.
        private static Window? _familyWindow;
        private static Window? _settingsWindow;
        private static readonly Dictionary<string, Window> _extWindows =
            new(StringComparer.OrdinalIgnoreCase);

        public static void Build(UIControlledApplication app, string launcherPath)
        {
            AppLog.Initialize();
            RibbonEventHub.Initialize();

            string revitVersion = app.ControlledApplication.VersionNumber; // year, e.g. "2025"
            Log.Information("Building ribbon for Revit {RevitVersion}", revitVersion);

            // Static buttons via the official API.
            RibbonPanel mainPanel = GetOrCreatePanel(app, DefaultTab, "Parameter");
            AddStaticButton(mainPanel, "AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME, launcherPath,
                MainCommandClass, "Open AnalyseTool", appIcon: "AnalyzeTool_Icon.png");

            // Second top-level button, sitting next to the main one: the Family Manager window.
            AddStaticButton(mainPanel, "AnalyseToolFamilies", "Family Manager", launcherPath,
                FamilyControlCommandClass, "Browse, audit and manage the families in this project",
                image: BuildGlyphIcon("")); // Segoe MDL2 "ViewAll" (grid)

            // Third button next to the others: the dockable placement palette (types grouped by family,
            // click a type to place it). Uses the same launcher slot pattern as the other static buttons.
            AddStaticButton(mainPanel, "AnalyseToolPalette", "Component", launcherPath,
                FamilyPaletteCommandClass, "Place a component — dockable family palette",
                image: BuildGlyphIcon("")); // Segoe MDL2 "ViewAll" (list)

            // Register the single dockable pane. Revit only permits pane registration during OnStartup,
            // which is why one always-present host pane is registered here and its content is swapped by
            // route — features and extensions appear in the dock without a Revit restart.
            DockPaneHost.Register(app);

            // Settings / Reload / Report-a-bug as one 3-high stacked column of small buttons.
            RibbonPanel managePanel = GetOrCreatePanel(app, DefaultTab, "Manage");

            PushButtonData settingsData = MakeButtonData("AnalyseToolSettings", "Settings", launcherPath,
                SettingsCommandClass, "Show where extensions live and how to add them");
            PushButtonData reloadData = MakeButtonData("AnalyseToolReload", "Reload", launcherPath,
                ReloadCommandClass, "Reload extensions (DLLs + buttons) without restarting Revit");
            PushButtonData bugsData = MakeButtonData("AnalyseToolBugs", "Report a bug", launcherPath,
                BugsCommandClass, "Report a bug or request a feature on GitHub");

            IList<RibbonItem> stacked = managePanel.AddStackedItems(settingsData, reloadData, bugsData);
            SetStackedImage(stacked, 0, BuildGlyphIcon("", 16)); // Settings (U+E713)
            SetStackedImage(stacked, 1, BuildGlyphIcon("", 16)); // Reload (U+E72C)
            SetStackedImage(stacked, 2, BuildGlyphIcon("", 16)); // Report a bug (U+EBE8)

            // Dynamic extension buttons via AdWindows.
            RefreshExtensionButtons(revitVersion);
        }

        /// <summary>Re-scans manifests and brings the AdWindows extension buttons in sync: adds new,
        /// removes gone, updates changed (text/icon/tooltip) and moves buttons whose ui.tab/ui.panel
        /// changed — all live. Safe to call repeatedly (Build + Reload).</summary>
        public static void RefreshExtensionButtons(string revitVersion)
        {
            if (AdWin.ComponentManager.Ribbon is null) return; // ribbon not ready yet

            List<ExtensionDescriptor> found = ExtensionCatalog
                .Scan(ExtensionSources.ScanDirs(revitVersion))
                .Where(d => d.HasUi)
                .ToList();

            HashSet<string> foundIds = new(found.Select(d => d.Manifest.Id), StringComparer.OrdinalIgnoreCase);

            RemoveStaleButtons(foundIds);
            foreach (ExtensionDescriptor descriptor in found)
                SyncButton(descriptor);

            RemoveEmptyPanelsAndTabs();
        }

        /// <summary>Removes the AdWindows buttons (and cached descriptors) of extensions that are no
        /// longer present in the latest scan.</summary>
        private static void RemoveStaleButtons(HashSet<string> foundIds)
        {
            foreach (string id in _extButtons.Keys.ToList())
            {
                if (foundIds.Contains(id)) continue;

                (AdWin.RibbonButton button, string panelKey) = _extButtons[id];
                if (_adwPanels.TryGetValue(panelKey, out AdWin.RibbonPanelSource? oldPanel))
                    oldPanel.Items.Remove(button);
                _extButtons.Remove(id);
                _descriptors.Remove(id);
            }
        }

        /// <summary>Brings a single extension's button in sync: creates it if new, otherwise updates its
        /// text/tooltip/icon and moves it when the manifest's tab/panel changed.</summary>
        private static void SyncButton(ExtensionDescriptor descriptor)
        {
            string id = descriptor.Manifest.Id;
            _descriptors[id] = descriptor; // refresh for click-time lookup

            ExtensionUi ui = descriptor.Manifest.Ui!;
            ExtensionButton info = ui.Button!;
            string tab = string.IsNullOrWhiteSpace(ui.Tab) ? DefaultTab : ui.Tab!;
            string panelName = string.IsNullOrWhiteSpace(ui.Panel) ? ExtensionsPanelTitle : ui.Panel!;
            string panelKey = tab + "\n" + panelName;

            AdWin.RibbonPanelSource? source = GetOrCreateAdwPanel(tab, panelName, panelKey);
            if (source is null) return;

            ImageSource? icon = LoadIcon(descriptor, info.Icon);

            if (_extButtons.TryGetValue(id, out (AdWin.RibbonButton Button, string PanelKey) entry))
                UpdateButton(id, entry, info, icon, source, panelKey);
            else
                _extButtons[id] = (CreateButton(id, info, icon, source), panelKey);
        }

        /// <summary>Updates an existing button in place, relocating it to a new panel if its key changed.</summary>
        private static void UpdateButton(string id, (AdWin.RibbonButton Button, string PanelKey) entry,
            ExtensionButton info, ImageSource? icon, AdWin.RibbonPanelSource target, string panelKey)
        {
            if (!string.Equals(entry.PanelKey, panelKey, StringComparison.Ordinal))
            {
                if (_adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? from))
                    from.Items.Remove(entry.Button);
                target.Items.Add(entry.Button);
                _extButtons[id] = (entry.Button, panelKey);
            }

            entry.Button.Text = info.Name;
            entry.Button.ToolTip = info.Tooltip;
            if (icon != null) { entry.Button.Image = icon; entry.Button.LargeImage = icon; }
        }

        /// <summary>Creates a new AdWindows button for an extension and adds it to the given panel.</summary>
        private static AdWin.RibbonButton CreateButton(string id, ExtensionButton info,
            ImageSource? icon, AdWin.RibbonPanelSource target)
        {
            // A button either INVOKES a command directly (command-only script extensions, where
            // ui.button.command is set) or OPENS the extension's WebView window (UI extensions).
            string? command = info.Command;
            RelayCommand handler = string.IsNullOrWhiteSpace(command)
                ? new RelayCommand(() => RibbonEventHub.Run(uiApp => OpenExtension(id, uiApp)))
                : new RelayCommand(() => RibbonEventHub.Run(uiApp =>
                    {
                        AnalyseToolBootstrap.Initialize(uiApp); // ensure the dispatcher is ready
                        InvokeSavedCommand(command!);           // fire-and-forget (no deadlock on the hub)
                    }));

            AdWin.RibbonButton button = new()
            {
                Id = $"AnalyseTool.Ext.{id}",
                Text = info.Name,
                ShowText = true,
                ShowImage = true,
                Size = AdWin.RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = info.Tooltip,
                CommandHandler = handler,
            };
            if (icon != null) { button.Image = icon; button.LargeImage = icon; }

            target.Items.Add(button);
            return button;
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
            if (!WebView2Runtime.EnsureOrWarn()) return;

            if (_settingsWindow is not null)
            {
                Restore(_settingsWindow);
                return;
            }

            Window window = new SettingsWindow();
            window.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow = window;
            window.Show();
        }

        /// <summary>Ribbon "Family Control" button — opens the family browser/QC window (#/families).
        /// Single instance: a second click focuses the existing window instead of opening another.</summary>
        public static void OpenFamilyControl(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;

            if (_familyWindow is not null)
            {
                Restore(_familyWindow);
                return;
            }

            Window window = new AnalyseTool.App.FamilyControlWindow();
            window.Closed += (_, _) => _familyWindow = null;
            _familyWindow = window;
            window.Show();
        }

        /// <summary>Brings an already-open window back to the foreground (restoring it if minimized).</summary>
        private static void Restore(Window window)
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        /// <summary>Ribbon "Palette" button — shows the dockable family placement palette (#/families-dock).
        /// Initializes the host first (so the pane's transport has a dispatcher) then shows/routes the pane.</summary>
        public static void ShowFamilyPalette(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;
            DockPaneHost.ShowRoute("#/families-dock");
        }

        public static void Reload(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            AnalyseToolBootstrap.ReloadExtensions();                                  // C# command DLLs
            RefreshExtensionButtons(uiApp.Application.VersionNumber);                 // ribbon buttons

            TaskDialog.Show("AnalyseTool — Reload", "Extensions reloaded.");
        }

        private static void OpenExtension(string id, UIApplication uiApp)
        {
            if (!_descriptors.TryGetValue(id, out ExtensionDescriptor? descriptor)) return;

            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;

            // A dockable extension shows inside the shared pane (toggle); otherwise it opens its own window.
            ExtensionUi? ui = descriptor.Manifest.Ui;
            if (ui?.Dockable == true)
            {
                DockPaneHost.ShowExtension(id, descriptor.Directory, ui.DevUrl, ui.EntryHtml);
                return;
            }

            // One window per extension id — a second click focuses the open one.
            if (_extWindows.TryGetValue(id, out Window? existing))
            {
                Restore(existing);
                return;
            }

            Window window = new ExtensionWindow(descriptor);
            window.Closed += (_, _) => _extWindows.Remove(id);
            _extWindows[id] = window;
            window.Show();
        }

        /// <summary>Dispatches a script-extension's command from a ribbon click and shows its result in a
        /// dialog. Fire-and-forget on purpose: it must NOT be awaited inside the RibbonEventHub handler,
        /// or the command's own RunInRevitAsync (queued on the RevitTaskHub external event) would
        /// deadlock waiting for the event we're currently inside.</summary>
        private static void InvokeSavedCommand(string commandName)
        {
            _ = ReportAsync();

            async Task ReportAsync()
            {
                try
                {
                    object? result = await AnalyseToolBootstrap.Dispatcher
                        .DispatchAsync(commandName, JValue.CreateNull(), CancellationToken.None);
                    string text = result is null
                        ? "(no result)"
                        : JToken.FromObject(result).ToString(Formatting.Indented);
                    ShowResult(commandName, Truncate(text, 4000));
                }
                catch (Exception ex)
                {
                    ShowResult(commandName, "Error: " + ex.Message);
                }
            }
        }

        private static void ShowResult(string title, string content) =>
            RibbonEventHub.Run(_ => TaskDialog.Show(title, content));

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max) + "\n…(truncated)";

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

        /// <summary>Builds a PushButtonData (name/text/tooltip) without adding it to a panel — so several
        /// can be combined into a stacked column via <c>RibbonPanel.AddStackedItems</c>.</summary>
        private static PushButtonData MakeButtonData(string name, string text, string assemblyPath,
            string className, string? tooltip)
        {
            PushButtonData data = new(name, text, assemblyPath, className);
            if (!string.IsNullOrWhiteSpace(tooltip)) data.ToolTip = tooltip;
            return data;
        }

        /// <summary>Sets the (small) image on one item returned by <c>AddStackedItems</c>.</summary>
        private static void SetStackedImage(IList<RibbonItem> items, int index, ImageSource image)
        {
            if (index >= 0 && index < items.Count && items[index] is PushButton button)
            {
                try { button.Image = image; }
                catch { /* icon is best-effort */ }
            }
        }

        private static void AddStaticButton(RibbonPanel panel, string name, string text, string assemblyPath,
            string className, string? tooltip, string? appIcon = null, ImageSource? image = null)
        {
            PushButtonData data = new(name, text, assemblyPath, className);
            if (!string.IsNullOrWhiteSpace(tooltip)) data.ToolTip = tooltip;

            if (panel.AddItem(data) is not PushButton pushButton) return;

            try
            {
                // Prefer a pre-rendered ImageSource (e.g. a glyph icon); otherwise load the packaged PNG.
                ImageSource? resolved = image;
                if (resolved is null && !string.IsNullOrWhiteSpace(appIcon))
                    resolved = new BitmapImage(new Uri($"pack://application:,,,/AnalyseTool.App;component/Resources/Icons/{appIcon}"));

                if (resolved is not null)
                {
                    pushButton.Image = resolved;
                    pushButton.LargeImage = resolved;
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

            string label = string.IsNullOrWhiteSpace(descriptor.Manifest.Ui?.Button?.Name)
                ? descriptor.Manifest.Id
                : descriptor.Manifest.Ui!.Button!.Name;
            return BuildDefaultIcon(label);
        }

        /// <summary>Renders a Segoe MDL2 Assets glyph (a Windows system icon font) onto a transparent
        /// 32×32 bitmap, so the static ribbon buttons get crisp vector-style icons with no asset files.</summary>
        /// <summary>Renders a Segoe MDL2 glyph at the given pixel size. Use 32 for large ribbon buttons,
        /// 16 for the small images of stacked buttons (otherwise a 32px image overflows the stacked row).</summary>
        private static ImageSource BuildGlyphIcon(string glyph, int size = 32)
        {
            double fontSize = size * 0.6875; // 22 at 32px, ~11 at 16px

            DrawingVisual visual = new();
            using (DrawingContext dc = visual.RenderOpen())
            {
                FormattedText text = new(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe MDL2 Assets"), fontSize, new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.0);
                dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            }

            RenderTargetBitmap bitmap = new(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
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
