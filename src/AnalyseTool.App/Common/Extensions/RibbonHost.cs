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
        private const string ScriptsCommandClass = "AnalyseTool.Launcher.RevitCommands.ScriptsCommand";
        private const string SettingsCommandClass = "AnalyseTool.Launcher.RevitCommands.SettingsCommand";
        private const string ReloadCommandClass = "AnalyseTool.Launcher.RevitCommands.ReloadCommand";
        private const string BugsCommandClass = "AnalyseTool.Launcher.RevitCommands.BugsCommand";
        private const string ExtensionsCommandClass = "AnalyseTool.Launcher.RevitCommands.ExtensionsCommand";
        private const string NewExtensionCommandClass = "AnalyseTool.Launcher.RevitCommands.NewExtensionCommand";
        private const string DefaultTab = "AnalyseTool";
        private const string ExtensionsPanelTitle = "Extensions";
        private const string PinnedPanelTitle = "Scripts";

        private static readonly HashSet<string> _createdTabs = new(StringComparer.OrdinalIgnoreCase);
        // "extension id\nbutton index" -> (button, key of the panel it currently sits in). Keyed per
        // BUTTON, not per extension: one manifest may declare several, and each is placed, moved and
        // removed on its own.
        private static readonly Dictionary<string, ExtEntry> _extButtons =
            new(StringComparer.OrdinalIgnoreCase);
        // Stacked columns we assembled per panel — rebuilt from scratch on every refresh, so they are
        // tracked only to be removed. Keyed like _adwPanels.
        private static readonly Dictionary<string, List<AdWin.RibbonRowPanel>> _packedRows =
            new(StringComparer.Ordinal);
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
        // one window per extension button.
        //
        // The plugin's own pages (Settings, Extensions, New extension), one window each.
        private static readonly Dictionary<string, SystemWindow> _systemWindows =
            new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Window> _extWindows =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The host's togglable buttons: key -> (display name, PushButton). Only the main
        /// button and Scripts — Reload, Settings and the rest of the Manage block are not here on
        /// purpose, so Settings always stays reachable.</summary>
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

            // The one large button: the tool itself.
            RibbonPanel mainPanel = GetOrCreatePanel(app, DefaultTab, "Parameter");
            RegisterStaticButton("AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME,
                AddStaticButton(mainPanel, "AnalyseToolMain", SharedData.ToolData.PLUGIN_NAME, launcherPath,
                    MainCommandClass, "Open AnalyseTool", appIcon: "AnalyzeTool_Icon.png"));

            // Register the single dockable pane. Revit only permits pane registration during OnStartup,
            // which is why one always-present host pane is registered here and its content is swapped by
            // route — features and extensions appear in the dock without a Revit restart.
            DockPaneHost.Register(app);

            // Everything else is small: two stacked columns of three in the Manage panel, so the tab
            // reads as one big button and a tidy block beside it. The split is by JOB, not by size:
            // the left column is what you run and make (scripts, extensions, a new one), the right
            // column is the plugin itself (reload, preferences, feedback). Extensions used to live
            // inside Settings — a manager you visit to work, buried under a page you configure once.
            RibbonPanel managePanel = GetOrCreatePanel(app, DefaultTab, "Manage");

            // The script launcher exists so that GENERATED commands do not each need a ribbon button of
            // their own — the ribbon holds one entry and the list behind it grows.
            PushButtonData scriptsData = MakeButtonData("AnalyseToolScripts", "Scripts", launcherPath,
                ScriptsCommandClass, "Find and run any registered command — including the ones an AI wrote");
            PushButtonData extensionsData = MakeButtonData("AnalyseToolExtensions", "Extensions", launcherPath,
                ExtensionsCommandClass, "Install, update and manage extensions");
            PushButtonData newExtensionData = MakeButtonData("AnalyseToolNewExtension", "New", launcherPath,
                NewExtensionCommandClass, "Create a new extension: a button, a page, C# commands");

            IList<RibbonItem> workStack = managePanel.AddStackedItems(scriptsData, extensionsData, newExtensionData);
            SetStackedImage(workStack, 0, BuildGlyphIcon("\uE943", 16)); // Scripts — Code (U+E943)
            SetStackedImage(workStack, 1, BuildGlyphIcon("\uEA86", 16)); // Extensions — Puzzle (U+EA86)
            SetStackedImage(workStack, 2, BuildGlyphIcon("\uECC8", 16)); // New — AddTo (U+ECC8)

            // Scripts is togglable like the main button (a user without scripts can hide it); the rest of
            // the block is not — Settings must always stay reachable, and Reload with it.
            RegisterStaticButton("AnalyseToolScripts", "Scripts", workStack.Count > 0 ? workStack[0] as PushButton : null);
            ApplyStaticButtonVisibility();

            PushButtonData reloadData = MakeButtonData("AnalyseToolReload", "Reload", launcherPath,
                ReloadCommandClass, "Reload extensions (DLLs + buttons) without restarting Revit");
            PushButtonData settingsData = MakeButtonData("AnalyseToolSettings", "Settings", launcherPath,
                SettingsCommandClass, "AI and everything else about the plugin itself");
            PushButtonData bugsData = MakeButtonData("AnalyseToolBugs", "Report a bug", launcherPath,
                BugsCommandClass, "Report a bug or request a feature on GitHub");

            IList<RibbonItem> pluginStack = managePanel.AddStackedItems(reloadData, settingsData, bugsData);
            SetStackedImage(pluginStack, 0, BuildGlyphIcon("\uE72C", 16)); // Reload (U+E72C)
            SetStackedImage(pluginStack, 1, BuildGlyphIcon("\uE713", 16)); // Settings (U+E713)
            SetStackedImage(pluginStack, 2, BuildGlyphIcon("\uEBE8", 16)); // Report a bug (U+EBE8)

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
            PackStacks();
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
        /// form one run (laid into columns of three by <see cref="PackStacks"/>, together with the
        /// other extensions' small buttons on the same panel); everything else stands alone. Buttons
        /// the user turned off in the launcher are dropped BEFORE grouping, so turning one off closes
        /// the gap instead of leaving a hole in a column.</summary>
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
                    while (++i < live.Count && live[i].Info.ResolvedKind == ButtonKind.Stacked)
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

                ExtEntry gone = _extButtons[key];
                if (gone.Item is not null && _adwPanels.TryGetValue(gone.PanelKey, out AdWin.RibbonPanelSource? oldPanel))
                    oldPanel.Items.Remove(gone.Item);
                _extButtons.Remove(key); // a stacked run leaves through PackStacks, which rebuilds the columns
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

                if (_extButtons.TryGetValue(group.Key, out ExtEntry? entry)
                    && string.Equals(entry.Signature, group.Signature, StringComparison.Ordinal))
                {
                    // Same content: at most it moved to another panel. A stacked run is not placed by
                    // this method at all — PackStacks lays it out wherever PanelKey now says.
                    if (!string.Equals(entry.PanelKey, panelKey, StringComparison.Ordinal))
                    {
                        if (entry.Item is not null)
                        {
                            if (_adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? from))
                                from.Items.Remove(entry.Item);
                            source.Items.Add(entry.Item);
                        }
                        _extButtons[group.Key] = entry with { PanelKey = panelKey };
                    }
                    continue;
                }

                if (_extButtons.TryGetValue(group.Key, out entry) && entry.Item is not null
                    && _adwPanels.TryGetValue(entry.PanelKey, out AdWin.RibbonPanelSource? previous))
                    previous.Items.Remove(entry.Item);

                if (first.ResolvedKind == ButtonKind.Stacked)
                {
                    // Small buttons are a PANEL's business, not the extension's: two extensions with one
                    // small button each want one column of two, not two columns of one. So the run is
                    // only remembered here; PackStacks builds the columns once every panel is known.
                    _extButtons[group.Key] = new ExtEntry(null, BuildStacked(descriptor, group), panelKey, group.Signature);
                    continue;
                }

                AdWin.RibbonItem built = BuildGroup(descriptor, group);
                source.Items.Add(built);
                _extButtons[group.Key] = new ExtEntry(built, null, panelKey, group.Signature);
            }
        }

        /// <summary>Builds the ribbon item for a group, by kind.</summary>
        private static AdWin.RibbonItem BuildGroup(ExtensionDescriptor descriptor, ButtonGroup group)
        {
            string id = descriptor.Manifest.Id;

            switch (group.First.ResolvedKind)
            {
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

        /// <summary>The small buttons of a stacked run, built but not placed — see <see cref="PackStacks"/>.</summary>
        private static List<StackedButton> BuildStacked(ExtensionDescriptor descriptor, ButtonGroup group)
        {
            string id = descriptor.Manifest.Id;
            List<StackedButton> buttons = new(group.Infos.Count);
            for (int n = 0; n < group.Infos.Count; n++)
            {
                ExtensionButton info = group.Infos[n];
                buttons.Add(new StackedButton(
                    MakeButton(id, group.Key + ":" + n, info, LoadIcon(descriptor, info.Icon), small: true),
                    info.Order, id, n));
            }
            return buttons;
        }

        /// <summary>
        /// Lays out every panel's small buttons as columns of three — the shape Revit's own
        /// <c>AddStackedItems</c> makes — regardless of which extension each button came from.
        ///
        /// Ownership and layout are different questions. An extension owns its buttons (they come and go
        /// with it), but a COLUMN is the panel's: the user who marks one button "small" in two
        /// extensions expects them under each other, and the manifest of either cannot say so. Rebuilt
        /// from scratch on every refresh, which is cheap and makes removal trivial — a run that left
        /// _extButtons is simply not there the next time the columns are built.
        ///
        /// Order within a panel: the button's <c>order</c>, then the extension id, then declaration —
        /// so one extension's consecutive small buttons stay together, and two extensions interleave
        /// only when their authors asked for it with explicit orders. Columns go after the panel's
        /// large items.
        /// </summary>
        private static void PackStacks()
        {
            foreach ((string panelKey, AdWin.RibbonPanelSource panel) in _adwPanels)
            {
                if (_packedRows.TryGetValue(panelKey, out List<AdWin.RibbonRowPanel>? old))
                {
                    foreach (AdWin.RibbonRowPanel row in old) panel.Items.Remove(row);
                    old.Clear();
                }

                List<StackedButton> buttons = _extButtons.Values
                    .Where(e => e.Stacked is not null && string.Equals(e.PanelKey, panelKey, StringComparison.Ordinal))
                    .SelectMany(e => e.Stacked!)
                    .OrderBy(b => b.Order)
                    .ThenBy(b => b.ExtensionId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.Index)
                    .ToList();
                if (buttons.Count == 0) continue;

                List<AdWin.RibbonRowPanel> rows = _packedRows.TryGetValue(panelKey, out List<AdWin.RibbonRowPanel>? list)
                    ? list : (_packedRows[panelKey] = new List<AdWin.RibbonRowPanel>());

                for (int i = 0; i < buttons.Count; i += 3)
                {
                    AdWin.RibbonRowPanel row = new();
                    for (int n = i; n < Math.Min(i + 3, buttons.Count); n++)
                    {
                        if (n > i) row.Items.Add(new AdWin.RibbonRowBreak());
                        row.Items.Add(buttons[n].Button);
                    }
                    panel.Items.Add(row);
                    rows.Add(row);
                }
            }
        }

        /// <summary>One ribbon entry of an extension: either an item placed directly on its panel
        /// (large button, pulldown) or a run of small buttons that <see cref="PackStacks"/> places.</summary>
        private sealed record ExtEntry(AdWin.RibbonItem? Item, List<StackedButton>? Stacked, string PanelKey, string Signature);

        /// <summary>A small button with what the packer sorts by.</summary>
        private sealed record StackedButton(AdWin.RibbonButton Button, int Order, string ExtensionId, int Index);

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

        /// <summary>Ribbon "Settings" button — the plugin's own preferences (AI, about).</summary>
        public static void OpenSettings(UIApplication uiApp) =>
            OpenSystemPage(uiApp, "settings", "#/system/settings", "AnalyseTool — Settings", 880, 720);

        /// <summary>Ribbon "Extensions" button — the extension manager (installed, catalog, dev folders).</summary>
        public static void OpenExtensions(UIApplication uiApp) =>
            OpenSystemPage(uiApp, "extensions", "#/system/extensions", "AnalyseTool — Extensions", 1000, 680);

        /// <summary>Ribbon "New" button — a small window with nothing but the create-extension form.
        /// Its own window, not the manager with a drawer over it: pressing "New" means "I want to make
        /// one", and a list of everything else is noise behind that.</summary>
        public static void OpenNewExtension(UIApplication uiApp) =>
            OpenSystemPage(uiApp, "new-extension", "#/system/new-extension", "AnalyseTool — New extension", 720, 840);

        /// <summary>Opens (or focuses) one of the plugin's own pages.</summary>
        private static void OpenSystemPage(UIApplication uiApp, string key, string route, string title,
            double width, double height)
        {
            AnalyseToolBootstrap.Initialize(uiApp);
            if (!WebView2Runtime.EnsureOrWarn()) return;

            if (_systemWindows.TryGetValue(key, out SystemWindow? existing))
            {
                Restore(existing);
                return;
            }

            SystemWindow window = new(route, title, width, height);
            window.Closed += (_, _) => _systemWindows.Remove(key);
            _systemWindows[key] = window;
            window.Show();
        }

        /// <summary>Brings an already-open window back to the foreground (restoring it if minimized).</summary>
        private static void Restore(Window window)
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        /// <summary>Ribbon "Scripts" button — shows the dockable command launcher (#/scripts). Same
        /// pattern the dockable extensions use: initialize the host so the pane's transport has a dispatcher,
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
        /// plugin.json), or renders a Segoe MDL2 glyph when the value is <c>glyph:E8A9</c>. Falls back
        /// to a generated default icon when neither works.
        /// <para>The glyph form exists because the host had a capability its extensions did not: the
        /// built-in buttons are drawn from the system icon font and stay crisp at any DPI, while an
        /// extension could only ship a PNG or accept a letter. Found while moving the Family Manager
        /// out — an extension replacing a built-in button could not reproduce its icon.</para></summary>
        private static ImageSource LoadIcon(ExtensionDescriptor descriptor, string? icon)
        {
            // "glyph:E8A9" — a Segoe MDL2 Assets code point, the same source the host's own buttons use.
            if (!string.IsNullOrWhiteSpace(icon) && icon!.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
            {
                string code = icon.Substring("glyph:".Length).Trim().TrimStart('U', 'u', '+');
                if (int.TryParse(code, System.Globalization.NumberStyles.HexNumber,
                                 System.Globalization.CultureInfo.InvariantCulture, out int cp))
                    return BuildGlyphIcon(char.ConvertFromUtf32(cp));
                // Unparseable code point falls through to the default icon rather than throwing: a
                // manifest typo must not cost the extension its button.
            }
            else if (!string.IsNullOrWhiteSpace(icon))
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
