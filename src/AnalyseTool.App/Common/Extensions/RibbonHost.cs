using AnalyseTool.Core.Common.Bootstrap;
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
        private const string ScriptsCommandClass = "AnalyseTool.Launcher.RevitCommands.ScriptsCommand";
        private const string SettingsCommandClass = "AnalyseTool.Launcher.RevitCommands.SettingsCommand";
        private const string ReloadCommandClass = "AnalyseTool.Launcher.RevitCommands.ReloadCommand";
        private const string BugsCommandClass = "AnalyseTool.Launcher.RevitCommands.BugsCommand";
        private const string DefaultTab = "AnalyseTool";
        private const string ExtensionsPanelTitle = "Extensions";
        private const string PinnedPanelTitle = "Scripts";

        private static readonly HashSet<string> _createdTabs = new(StringComparer.OrdinalIgnoreCase);
        // "extension id\nbutton index" -> (button, key of the panel it currently sits in). Keyed per
        // BUTTON, not per extension: one manifest may declare several, and each is placed, moved and
        // removed on its own.
        private static readonly Dictionary<string, (AdWin.RibbonItem Item, string PanelKey, string Signature)> _extButtons =
            new(StringComparer.OrdinalIgnoreCase);
        // command name -> its button, for commands the USER pinned in the launcher. Kept apart from
        // _extButtons because the two are keyed differently and answer to different owners: a manifest
        // button belongs to an extension, while a pin names a single command and may name one from an
        // extension whose manifest we must not write, or from no extension at all.
        private static readonly Dictionary<string, (AdWin.RibbonButton Button, string PanelKey)> _pinnedButtons =
            new(StringComparer.OrdinalIgnoreCase);
        // Built once, on the UI thread, the first time a pinned button needs it.
        private static ImageSource? _pinnedIcon;
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

        /// <summary>The host's togglable main buttons: key -> (display name, PushButton). The Manage
        /// stack is not here on purpose — Settings must always stay reachable.</summary>
        private static readonly Dictionary<string, (string Name, PushButton Button)> _staticButtons =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Keys + display names of the togglable host buttons, for the Settings listing.</summary>
        public static IReadOnlyList<(string Key, string Name)> StaticButtonInfos() =>
            _staticButtons.Select(kv => (kv.Key, kv.Value.Name)).ToList();

        /// <summary>Re-applies <see cref="HostButtonState"/> to the host buttons. Revit UI thread only.</summary>
        public static void ApplyStaticButtonVisibility()
        {
            foreach ((string key, (_, PushButton button)) in _staticButtons)
            {
                try { button.Visible = HostButtonState.IsVisible(key); }
                catch { /* ribbon may not be ready; next Build applies the state anyway */ }
            }
        }

        public static void Build(UIControlledApplication app, string launcherPath)
        {
            AppLog.Initialize();
            RibbonEventHub.Initialize();

            string revitVersion = app.ControlledApplication.VersionNumber; // year, e.g. "2025"
            Log.Information("Building ribbon for Revit {RevitVersion}", revitVersion);

            // Static buttons via the official API.
            RibbonPanel mainPanel = GetOrCreatePanel(app, DefaultTab, "Parameter");
            RegisterStaticButton("AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME,
                AddStaticButton(mainPanel, "AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME, launcherPath,
                    MainCommandClass, "Open AnalyseTool", appIcon: "AnalyzeTool_Icon.png"));

            // Second top-level button, sitting next to the main one: the Family Manager window.
            RegisterStaticButton("AnalyseToolFamilies", "Family Manager",
                AddStaticButton(mainPanel, "AnalyseToolFamilies", "Family Manager", launcherPath,
                    FamilyControlCommandClass, "Browse, audit and manage the families in this project",
                    image: BuildGlyphIcon(""))); // Segoe MDL2 "ViewAll" (grid)

            // Third button next to the others: the dockable placement palette (types grouped by family,
            // click a type to place it). Uses the same launcher slot pattern as the other static buttons.
            RegisterStaticButton("AnalyseToolPalette", "Component",
                AddStaticButton(mainPanel, "AnalyseToolPalette", "Component", launcherPath,
                    FamilyPaletteCommandClass, "Place a component — dockable family palette",
                    image: BuildGlyphIcon(""))); // Segoe MDL2 "ViewAll" (list)

            // Fourth button: the script launcher. It exists so that GENERATED commands do not each need
            // a ribbon button of their own — the ribbon holds one entry and the list behind it grows.
            RegisterStaticButton("AnalyseToolScripts", "Scripts",
                AddStaticButton(mainPanel, "AnalyseToolScripts", "Scripts", launcherPath,
                    ScriptsCommandClass, "Find and run any registered command — including the ones an AI wrote",
                    image: BuildGlyphIcon("\uE943"))); // Segoe MDL2 "Code"

            ApplyStaticButtonVisibility();

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

            // Incompatible extensions (a declared DLL with no build for this Revit year) and
            // user-disabled ones get no button: their commands never load, so the UI would only
            // produce dead invokes. Both stay visible in the Settings listing.
            List<ExtensionDescriptor> found = ExtensionCatalog
                .Scan(revitVersion)
                .Where(d => d.HasUi && d.IsCompatibleWithHost && ExtensionStateStore.IsEnabled(d.Manifest.Id))
                .ToList();

            // Every button the manifests ask for, by ribbon key — the set the ribbon must end up
            // holding. A button the user turned off in the launcher simply never enters it.
            HashSet<string> wantedKeys = new(
                found.SelectMany(GroupButtons).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

            RemoveStaleButtons(wantedKeys);
            foreach (ExtensionDescriptor descriptor in found)
                SyncButtons(descriptor);

            RefreshPinnedButtons(found);
            RemoveEmptyPanelsAndTabs();
        }

        /// <summary>One ribbon item to build: a single button, or a run of stacked ones sharing a row.
        /// Grouping lives in ONE place because two passes need the same answer — the pass that decides
        /// which ribbon items must exist, and the pass that builds them.</summary>
        private sealed record ButtonGroup(string Key, IReadOnlyList<ExtensionButton> Infos, string Signature)
        {
            public ExtensionButton First => Infos[0];
        }

        /// <summary>Splits a manifest's buttons into ribbon items. Consecutive <c>stacked</c> entries
        /// fill rows of three (the Revit convention); everything else stands alone. Buttons the user
        /// turned off in the launcher are dropped BEFORE grouping, so turning one off closes the gap
        /// instead of leaving a hole in a row.</summary>
        private static List<ButtonGroup> GroupButtons(ExtensionDescriptor descriptor)
        {
            string id = descriptor.Manifest.Id;
            List<(ExtensionButton Info, int Index)> live = descriptor.Manifest.Ui!.EffectiveButtons()
                .Select((b, i) => (Info: b, Index: i))
                .Where(t => !UserTookButtonAway(t.Info))
                .ToList();

            List<ButtonGroup> groups = new();
            for (int i = 0; i < live.Count;)
            {
                List<ExtensionButton> infos = new() { live[i].Info };
                int firstIndex = live[i].Index;
                if (live[i].Info.ResolvedKind == ButtonKind.Stacked)
                    while (++i < live.Count && live[i].Info.ResolvedKind == ButtonKind.Stacked && infos.Count < 3)
                        infos.Add(live[i].Info);
                else
                    i++;

                groups.Add(new ButtonGroup(ButtonKey(id, firstIndex), infos, Signature(infos)));
            }
            return groups;
        }

        /// <summary>Everything about a group that, when changed, means the ribbon item must be rebuilt
        /// rather than relabelled. Cheap to compute and compared as one string.</summary>
        private static string Signature(IReadOnlyList<ExtensionButton> infos) =>
            string.Join("|", infos.Select(b =>
                $"{b.ResolvedKind}:{b.Name}:{b.Tooltip}:{b.Icon}:{b.Command}:" +
                string.Join(",", (b.Items ?? Array.Empty<ExtensionButton>()).Select(c => c.Name + "/" + c.Command))));

        /// <summary>Ribbon key for one manifest button. The index is the identity: renaming a button
        /// keeps its place on the ribbon, and only reordering moves it.</summary>
        /// <summary>Separator inside composite ribbon keys. A newline cannot occur in an extension
        /// id, a tab name or a panel name, so a composite key can never be ambiguous.</summary>
        private const string KeySeparator = "\n";

        private static string ButtonKey(string extensionId, int index) => extensionId + KeySeparator + index;

        /// <summary>A manifest button the user turned off in the launcher. Only COMMAND buttons can be
        /// turned off there — the launcher lists commands, and a button that opens a page is not one,
        /// so a UI extension keeps its button no matter what the store says about its commands.</summary>
        private static bool UserTookButtonAway(ExtensionButton button)
        {
            string? command = button.Command;
            return !string.IsNullOrWhiteSpace(command) && CommandButtons.Override(command!) == false;
        }

        /// <summary>
        /// Brings the pinned-command buttons in sync — the ones the user asked for in the launcher.
        ///
        /// Runs from the same refresh as the manifest pass so every existing trigger (startup, Reload,
        /// install/remove) covers it, and takes that pass's result so one command cannot end up with
        /// two buttons: an author who already put it on the ribbon wins, and the pin adds nothing.
        /// </summary>
        private static void RefreshPinnedButtons(IReadOnlyCollection<ExtensionDescriptor> shownExtensions)
        {
            HashSet<string> alreadyShown = new(
                shownExtensions.SelectMany(d => d.Manifest.Ui!.EffectiveButtons())
                    .Select(b => b.Command)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!),
                StringComparer.OrdinalIgnoreCase);

            List<CommandButtonPin> pins = CommandButtons.Pinned()
                .Where(p => !alreadyShown.Contains(p.Command) && IsRegisteredOrUnknown(p.Command))
                .ToList();

            HashSet<string> wanted = new(pins.Select(p => p.Command), StringComparer.OrdinalIgnoreCase);
            foreach (string command in _pinnedButtons.Keys.ToList())
            {
                if (wanted.Contains(command)) continue;

                (AdWin.RibbonButton button, string panelKey) = _pinnedButtons[command];
                if (_adwPanels.TryGetValue(panelKey, out AdWin.RibbonPanelSource? panel))
                    panel.Items.Remove(button);
                _pinnedButtons.Remove(command);
            }

            foreach (CommandButtonPin pin in pins)
                SyncPinnedButton(pin);
        }

        /// <summary>Whether a pinned command still exists. The ribbon is built at Revit startup, before
        /// the platform has registered anything, so "cannot tell yet" has to mean SHOW: a button that
        /// appears and is pruned on the first reload beats one that is missing until the user happens
        /// to click something else.</summary>
        private static bool IsRegisteredOrUnknown(string command) =>
            !CoreServices.IsInitialized || CoreServices.Queue.IsRegistered(command);

        /// <summary>Creates or refreshes one pinned command's button.</summary>
        private static void SyncPinnedButton(CommandButtonPin pin)
        {
            string panelKey = DefaultTab + "\n" + PinnedPanelTitle;

            if (_pinnedButtons.TryGetValue(pin.Command, out (AdWin.RibbonButton Button, string PanelKey) entry))
            {
                entry.Button.Text = pin.Label;
                entry.Button.ToolTip = pin.Tooltip;
                return;
            }

            AdWin.RibbonPanelSource? source = GetOrCreateAdwPanel(DefaultTab, PinnedPanelTitle, panelKey);
            if (source is null) return;

            _pinnedIcon ??= BuildGlyphIcon("\uE943"); // Segoe MDL2 "Code" — same mark as the launcher

            AdWin.RibbonButton button = new()
            {
                Id = "AnalyseTool.Pin." + pin.Command,
                Text = pin.Label,
                ShowText = true,
                ShowImage = true,
                Size = AdWin.RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = pin.Tooltip,
                Image = _pinnedIcon,
                LargeImage = _pinnedIcon,
                // A pin can only be made from the launcher, which lists scripts and built-ins alone, so
                // the command behind one is always something that window can show again.
                CommandHandler = new RelayCommand(() =>
                    RibbonEventHub.Run(uiApp => RunCommandFromRibbon(pin.Command, uiApp, canOpenLauncher: true))),
            };

            source.Items.Add(button);
            _pinnedButtons[pin.Command] = (button, panelKey);
        }

        /// <summary>Removes the AdWindows buttons (and cached descriptors) of extensions that are no
        /// longer present in the latest scan.</summary>
        private static void RemoveStaleButtons(HashSet<string> wantedKeys)
        {
            foreach (string key in _extButtons.Keys.ToList())
            {
                if (wantedKeys.Contains(key)) continue;

                (AdWin.RibbonItem item, string panelKey, _) = _extButtons[key];
                if (_adwPanels.TryGetValue(panelKey, out AdWin.RibbonPanelSource? oldPanel))
                    oldPanel.Items.Remove(item);
                _extButtons.Remove(key);
            }

            // A descriptor is dropped only when the extension has NO buttons left, not when one of
            // several went away.
            foreach (string id in _descriptors.Keys.ToList())
                if (!_extButtons.Keys.Any(k => k.StartsWith(id + KeySeparator, StringComparison.OrdinalIgnoreCase)))
                    _descriptors.Remove(id);
        }

        /// <summary>Brings one extension's ribbon items in sync. A group whose content changed is
        /// rebuilt rather than patched: a row of stacked buttons or a pulldown has children, and
        /// editing those in place is more code than recreating them on a Reload nobody watches.</summary>
        private static void SyncButtons(ExtensionDescriptor descriptor)
        {
            string id = descriptor.Manifest.Id;
            _descriptors[id] = descriptor; // refresh for click-time lookup

            ExtensionUi ui = descriptor.Manifest.Ui!;

            foreach (ButtonGroup group in GroupButtons(descriptor))
            {
                ExtensionButton first = group.First;

                // Placement falls back from the button to the extension to the host default, so a
                // single-button manifest never repeats what ui.tab / ui.panel already said. A stacked
                // run takes its placement from the first entry — a row cannot straddle two panels.
                string tab = Coalesce(first.Tab, ui.Tab, DefaultTab);
                string panelName = Coalesce(first.Panel, ui.Panel, ExtensionsPanelTitle);
                string panelKey = tab + KeySeparator + panelName;

                AdWin.RibbonPanelSource? source = GetOrCreateAdwPanel(tab, panelName, panelKey);
                if (source is null) continue;

                if (_extButtons.TryGetValue(group.Key, out (AdWin.RibbonItem Item, string PanelKey, string Signature) entry)
                    && string.Equals(entry.Signature, group.Signature, StringComparison.Ordinal))
                {
                    // Same content: at most it moved to another panel.
                    if (!string.Equals(entry.PanelKey, panelKey, StringComparison.Ordinal))
                    {
                        if (_adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? from))
                            from.Items.Remove(entry.Item);
                        source.Items.Add(entry.Item);
                        _extButtons[group.Key] = (entry.Item, panelKey, entry.Signature);
                    }
                    continue;
                }

                if (_extButtons.TryGetValue(group.Key, out entry)
                    && _adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? previous))
                    previous.Items.Remove(entry.Item);

                AdWin.RibbonItem built = BuildGroup(descriptor, group);
                source.Items.Add(built);
                _extButtons[group.Key] = (built, panelKey, group.Signature);
            }
        }

        /// <summary>Builds the ribbon item for a group, by kind.</summary>
        private static AdWin.RibbonItem BuildGroup(ExtensionDescriptor descriptor, ButtonGroup group)
        {
            string id = descriptor.Manifest.Id;

            switch (group.First.ResolvedKind)
            {
                case ButtonKind.Stacked:
                {
                    // Small buttons stacked in one column — the shape Revit's own AddStackedItems makes.
                    AdWin.RibbonRowPanel row = new();
                    for (int n = 0; n < group.Infos.Count; n++)
                    {
                        if (n > 0) row.Items.Add(new AdWin.RibbonRowBreak());
                        ExtensionButton info = group.Infos[n];
                        row.Items.Add(MakeButton(id, group.Key + ":" + n, info,
                            LoadIcon(descriptor, info.Icon), small: true));
                    }
                    return row;
                }

                case ButtonKind.Pulldown:
                {
                    ExtensionButton info = group.First;
                    AdWin.RibbonSplitButton pulldown = new()
                    {
                        Id = "AnalyseTool.Ext." + group.Key.Replace(KeySeparator, "."),
                        Text = info.Name,
                        ShowText = true,
                        ShowImage = true,
                        Size = AdWin.RibbonItemSize.Large,
                        Orientation = System.Windows.Controls.Orientation.Vertical,
                        ToolTip = info.Tooltip,
                        // A pulldown, not a split button: the head opens the list rather than running
                        // the first entry, which is what an author asking for "pulldown" means.
                        IsSplit = false,
                        IsSynchronizedWithCurrentItem = false,
                    };
                    ImageSource? head = LoadIcon(descriptor, info.Icon);
                    if (head != null) { pulldown.Image = head; pulldown.LargeImage = head; }

                    // Children are NOT in EffectiveButtons() — they live inside their parent — so they
                    // get a key derived from it rather than a position in that list.
                    IReadOnlyList<ExtensionButton> children = info.Items ?? Array.Empty<ExtensionButton>();
                    for (int n = 0; n < children.Count; n++)
                        pulldown.Items.Add(MakeButton(id, group.Key + ":" + n, children[n],
                            LoadIcon(descriptor, children[n].Icon), small: true));
                    return pulldown;
                }

                default:
                    return MakeButton(id, group.Key, group.First,
                        LoadIcon(descriptor, group.First.Icon), small: false);
            }
        }

        /// <summary>First non-blank of the three: a per-button value overrides the extension's, which
        /// overrides the host default.</summary>
        private static string Coalesce(string? button, string? extension, string fallback) =>
            !string.IsNullOrWhiteSpace(button) ? button!
            : !string.IsNullOrWhiteSpace(extension) ? extension!
            : fallback;

        /// <summary>Creates one AdWindows button. Does NOT add it anywhere — the caller decides
        /// whether it goes on a panel, into a stacked row or under a pulldown.</summary>
        /// <param name="small">Small buttons sit in rows and under pulldowns; large ones stand alone.</param>
        private static AdWin.RibbonButton MakeButton(string id, string key, ExtensionButton info,
            ImageSource? icon, bool small)
        {
            // A button either INVOKES a command directly (command-only script extensions, where
            // command is set) or OPENS the extension's page (UI extensions).
            string? command = info.Command;
            RelayCommand handler = string.IsNullOrWhiteSpace(command)
                ? new RelayCommand(() => RibbonEventHub.Run(uiApp => OpenExtension(id, key, info, uiApp)))
                : new RelayCommand(() => RibbonEventHub.Run(uiApp =>
                    RunCommandFromRibbon(command!, uiApp, LauncherLists(id))));

            AdWin.RibbonButton button = new()
            {
                Id = "AnalyseTool.Ext." + key.Replace(KeySeparator, "."),
                Text = info.Name,
                ShowText = true,
                ShowImage = true,
                Size = small ? AdWin.RibbonItemSize.Standard : AdWin.RibbonItemSize.Large,
                Orientation = small
                    ? System.Windows.Controls.Orientation.Horizontal
                    : System.Windows.Controls.Orientation.Vertical,
                ToolTip = info.Tooltip,
                CommandHandler = handler,
            };
            if (icon != null) { button.Image = icon; button.LargeImage = icon; }
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

            Window window = new AnalyseTool.App.Common.Extensions.FamilyControlWindow();
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

        /// <summary>Ribbon "Scripts" button — shows the dockable command launcher (#/scripts). Same
        /// pattern as the family palette: initialize the host so the pane's transport has a dispatcher,
        /// then route the single registered pane.</summary>
        public static void ShowScriptLauncher(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;
            DockPaneHost.ShowRoute("#/scripts");
        }

        public static void Reload(UIApplication uiApp)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            CoreServices.ReloadExtensions();                                  // C# command DLLs
            RefreshExtensionButtons(uiApp.Application.VersionNumber);                 // ribbon buttons

            TaskDialog.Show("AnalyseTool — Reload", "Extensions reloaded.");
        }

        private static void OpenExtension(string id, string key, ExtensionButton info, UIApplication uiApp)
        {
            if (!_descriptors.TryGetValue(id, out ExtensionDescriptor? descriptor)) return;

            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;

            ExtensionUi? ui = descriptor.Manifest.Ui;

            // Entry page and dockability are properties of the SURFACE, not of the extension: a manager
            // opens as a window while its placement palette belongs in the dock. The button decides; the
            // extension-level values are the fallback for the single-button form. The button object is
            // captured at build time, so a pulldown child — which appears in no top-level list — is
            // resolved exactly like any other.
            string entryHtml = !string.IsNullOrWhiteSpace(info.EntryHtml) ? info.EntryHtml!
                : ui?.EntryHtml ?? "index.html";
            bool dockable = info.Dockable ?? ui?.Dockable ?? false;

            if (dockable)
            {
                DockPaneHost.ShowExtension(id, descriptor.Directory, ui?.DevUrl, entryHtml, key);
                return;
            }

            // One window per button — a second click focuses the open one.
            if (_extWindows.TryGetValue(key, out Window? existing))
            {
                Restore(existing);
                return;
            }

            Window window = new ExtensionWindow(descriptor, entryHtml);
            window.Closed += (_, _) => _extWindows.Remove(key);
            _extWindows[key] = window;
            window.Show();
        }

        /// <summary>Whether the script launcher would list this command's extension. It shows generated
        /// scripts that stand on their own, so neither a compiled extension nor one whose button opens a
        /// page may be sent there — the user would land on a window that refuses to show it.
        /// <para>Must stay in step with <c>listable()</c> in ScriptLauncherView.vue; both read the same
        /// two facts, and this side is the one that decides whether to navigate at all.</para></summary>
        private static bool LauncherLists(string source) =>
            // "core" is not an extension id and never will be in _descriptors, so it has to be named:
            // the window stopped listing built-ins, and a predicate called LauncherLists must not claim
            // otherwise just because the lookup missed.
            !string.Equals(source, "core", StringComparison.Ordinal)
            && (!_descriptors.TryGetValue(source, out ExtensionDescriptor? descriptor)
                || (!descriptor.DeclaresDll && !descriptor.OpensPage));

        /// <summary>
        /// What a ribbon button for a COMMAND does — whether the author declared it in a manifest or
        /// the user pinned it in the launcher.
        ///
        /// A command that takes no arguments is simply run. One that takes them cannot be: a click has
        /// no arguments to give, and dispatching with an empty payload is precisely how a command with
        /// an optional filter quietly returns nothing and reads as "no matches". So it opens the
        /// launcher with that command selected, where the form is built from its input schema.
        ///
        /// Unless the launcher does not list it. A compiled extension is absent from that window by
        /// design, so its own button falls back to running the command as it always has — an author who
        /// points a button at a command that needs arguments is choosing that, and it is not this
        /// method's place to invent a form the extension never shipped.
        /// </summary>
        private static void RunCommandFromRibbon(string commandName, UIApplication uiApp, bool canOpenLauncher)
        {
            AnalyseToolBootstrap.Initialize(uiApp); // ensure the dispatcher is ready

            Core.Common.Dispatch.CommandRegistration? registration =
                CoreServices.Queue.GetRegistration(commandName);
            if (registration is null)
            {
                TaskDialog.Show("AnalyseTool", $"'{commandName}' is not registered. Its extension may " +
                                               "have been removed, disabled, or failed to load.");
                return;
            }

            // Asked of the command that is actually registered, not only of the caller. A PIN passes true
            // because pins can only be made from the launcher — but a pin outlives the listing it was made
            // from: give an extension a page, and yesterday's pin now points at a command that window no
            // longer shows. Re-checking here means one answer for both callers and no stranded button.
            if (!canOpenLauncher || !LauncherLists(registration.Source) || !TakesArguments(registration))
            {
                InvokeSavedCommand(commandName); // fire-and-forget (no deadlock on the hub)
                return;
            }

            if (!WebView2Runtime.EnsureOrWarn()) return;
            DockPaneHost.ShowRoute("#/scripts?command=" + Uri.EscapeDataString(commandName));
        }

        /// <summary>Whether the command declares any input. A command with no InputType is left with
        /// the empty-object schema, which is exactly "takes nothing".</summary>
        private static bool TakesArguments(Core.Common.Dispatch.CommandRegistration registration)
        {
            try
            {
                return JObject.Parse(registration.InputSchemaJson)["properties"] is JObject properties
                       && properties.Count > 0;
            }
            catch (JsonException)
            {
                return false; // an unreadable schema is no reason to refuse to run the command
            }
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
                    object? result = await CoreServices.Queue.ExecuteAsync(
                        new Core.Common.Dispatch.CommandRequest(commandName, JValue.CreateNull(), "ribbon"));
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

        /// <summary>Remembers a togglable host button so Settings can list it and
        /// <see cref="ApplyStaticButtonVisibility"/> can show/hide it live.</summary>
        private static void RegisterStaticButton(string key, string displayName, PushButton? button)
        {
            if (button is not null)
                _staticButtons[key] = (displayName, button);
        }

        private static PushButton? AddStaticButton(RibbonPanel panel, string name, string text, string assemblyPath,
            string className, string? tooltip, string? appIcon = null, ImageSource? image = null)
        {
            PushButtonData data = new(name, text, assemblyPath, className);
            if (!string.IsNullOrWhiteSpace(tooltip)) data.ToolTip = tooltip;

            if (panel.AddItem(data) is not PushButton pushButton) return null;

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

            return pushButton;
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
                    if (File.Exists(path))
                    {
                        // OnLoad + Freeze: read the bytes NOW and release the file handle. The default
                        // (OnDemand) keeps the PNG locked for the button's lifetime, which would make
                        // uninstall/update of the extension folder fail until Revit restarts.
                        BitmapImage bitmap = new();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(path);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                }
                catch { /* fall through to the default */ }
            }

            string? firstName = descriptor.Manifest.Ui?.EffectiveButtons().FirstOrDefault()?.Name;
            string label = string.IsNullOrWhiteSpace(firstName) ? descriptor.Manifest.Id : firstName!;
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
