using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnalyseTool.Extensions.Catalog;
using AnalyseTool.Extensions.Loading;
using AnalyseTool.Extensions.Manifest;
using Microsoft.Extensions.Logging;
using AdWin = Autodesk.Windows;
using Color = System.Windows.Media.Color;
using FormattedText = System.Windows.Media.FormattedText;
using Point = System.Windows.Point;

namespace AnalyseTool.Infrastructure.Extensions;

/// <summary>
///     The ribbon buttons of the extensions: built with the AdWindows API, because the Revit API cannot add or remove buttons
///     after startup. A new extension gets its buttons at once; a removed one loses them; a changed one is rebuilt — whenever
///     <see cref="ExtensionHost.Changed" /> says so.
/// </summary>
/// <remarks>
///     <para>Placement: <c>tab</c> and <c>panel</c> of the button, then of <c>ui</c>, then the AnalyseTool tab and its «Extensions» panel.</para>
///     <para>
///         Small (<c>stacked</c>) buttons of every extension on one panel are laid out together in columns of three; large
///         ones come first. Within a panel: <c>order</c> (0 last), then the id, then the declaration.
///     </para>
///     <para>Only buttons, panels and tabs created here are ever removed.</para>
/// </remarks>
public sealed partial class ExtensionRibbon(ExtensionHost host, ExtensionButtonRunner runner, ILogger<ExtensionRibbon> logger)
{
    /// <summary>The tab of the add-in.</summary>
    public const string DefaultTab = "AnalyseTool";

    /// <summary>The panel of buttons that name none.</summary>
    public const string DefaultPanel = "Extensions";

    private const string Separator = "\n";

    private readonly Dictionary<string, AdWin.RibbonPanelSource> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdWin.RibbonItem>> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _createdTabs = new(StringComparer.Ordinal);
    private string _signature = "";

    /// <summary>Builds the buttons now and follows every change of the extensions.</summary>
    public void Start()
    {
        host.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(Refresh);
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(Refresh);
    }

    /// <summary>Brings the ribbon in line with the loaded extensions. Runs on the WPF thread of Revit.</summary>
    public void Refresh()
    {
        try
        {
            if (AdWin.ComponentManager.Ribbon is null)
            {
                return;
            }

            var extensions = host.GetStates()
                .Where(state => state.Enabled && state.Descriptor.HasButtons)
                .Select(state => state.Descriptor)
                .ToList();

            // Nothing that the ribbon shows changed: a reload of unchanged code keeps the buttons as they are
            var signature = string.Join("|", extensions.Select(Signature));
            if (signature == _signature)
            {
                return;
            }

            _signature = signature;
            Clear();
            Build(extensions);
            RemoveEmptyPanelsAndTabs();
        }
        catch (Exception exception)
        {
            LogRefreshFailed(logger, exception);
        }
    }

    private void Build(IReadOnlyList<ExtensionDescriptor> extensions)
    {
        var entries = new List<Entry>();
        foreach (var extension in extensions)
        {
            var ui = extension.Manifest.Ui!;
            var buttons = ui.EffectiveButtons();
            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                var panelKey = Coalesce(button.Tab, ui.Tab, DefaultTab) + Separator + Coalesce(button.Panel, ui.Panel, DefaultPanel);
                entries.Add(new Entry(extension, button, panelKey, index));
            }
        }

        foreach (var panel in entries.GroupBy(entry => entry.PanelKey))
        {
            var source = GetOrCreatePanel(panel.Key);
            if (source is null)
            {
                continue;
            }

            var ordered = panel.OrderBy(entry => entry.Button.Order == 0 ? int.MaxValue : entry.Button.Order)
                .ThenBy(entry => entry.Extension.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Index)
                .ToList();
            var items = _items[panel.Key] = [];

            foreach (var entry in ordered.Where(entry => entry.Button.ResolvedKind != ButtonKind.Stacked))
            {
                var item = entry.Button.ResolvedKind == ButtonKind.Pulldown ? MakePulldown(entry) : MakeButton(entry.Extension, entry.Button, small: false);
                source.Items.Add(item);
                items.Add(item);
            }

            var small = ordered.Where(entry => entry.Button.ResolvedKind == ButtonKind.Stacked).ToList();
            for (var start = 0; start < small.Count; start += 3)
            {
                var row = new AdWin.RibbonRowPanel();
                for (var index = start; index < Math.Min(start + 3, small.Count); index++)
                {
                    if (index > start)
                    {
                        row.Items.Add(new AdWin.RibbonRowBreak());
                    }

                    row.Items.Add(MakeButton(small[index].Extension, small[index].Button, small: true));
                }

                source.Items.Add(row);
                items.Add(row);
            }
        }
    }

    private AdWin.RibbonItem MakePulldown(Entry entry)
    {
        var pulldown = new AdWin.RibbonSplitButton
        {
            Id = $"AnalyseTool.Ext.{entry.Extension.Id}.{entry.Index}",
            Text = entry.Button.Name,
            ShowText = true,
            ShowImage = true,
            Size = AdWin.RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = entry.Button.Tooltip,
            IsSplit = false,
            IsSynchronizedWithCurrentItem = false
        };
        var icon = LoadIcon(entry.Extension, entry.Button);
        pulldown.Image = icon;
        pulldown.LargeImage = icon;
        foreach (var child in entry.Button.Items ?? [])
        {
            pulldown.Items.Add(MakeButton(entry.Extension, child, small: true));
        }

        return pulldown;
    }

    private AdWin.RibbonButton MakeButton(ExtensionDescriptor extension, ExtensionButton button, bool small)
    {
        var icon = LoadIcon(extension, button);
        return new AdWin.RibbonButton
        {
            Id = $"AnalyseTool.Ext.{extension.Id}.{button.Name}",
            Text = button.Name,
            ShowText = true,
            ShowImage = true,
            Size = small ? AdWin.RibbonItemSize.Standard : AdWin.RibbonItemSize.Large,
            Orientation = small ? System.Windows.Controls.Orientation.Horizontal : System.Windows.Controls.Orientation.Vertical,
            ToolTip = string.IsNullOrWhiteSpace(button.Tooltip) ? button.Name : button.Tooltip,
            Image = icon,
            LargeImage = icon,
            CommandHandler = new ClickCommand(() => runner.Click(extension, button))
        };
    }

    private AdWin.RibbonPanelSource? GetOrCreatePanel(string key)
    {
        if (_panels.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var ribbon = AdWin.ComponentManager.Ribbon;
        if (ribbon is null)
        {
            return null;
        }

        var parts = key.Split(Separator);
        var tab = ribbon.Tabs.FirstOrDefault(candidate => candidate.Title == parts[0] || candidate.Id == parts[0]);
        if (tab is null)
        {
            tab = new AdWin.RibbonTab { Title = parts[0], Id = "AnalyseTool.ExtTab." + parts[0], IsVisible = true, IsEnabled = true };
            ribbon.Tabs.Add(tab);
            _createdTabs.Add(parts[0]);
        }

        var source = new AdWin.RibbonPanelSource { Title = parts[1], Id = "AnalyseTool.ExtPanel." + key.Replace(Separator, ".") };
        tab.Panels.Add(new AdWin.RibbonPanel { Source = source });
        _panels[key] = source;
        return source;
    }

    private void Clear()
    {
        foreach (var (key, items) in _items)
        {
            if (_panels.TryGetValue(key, out var source))
            {
                foreach (var item in items)
                {
                    source.Items.Remove(item);
                }
            }
        }

        _items.Clear();
    }

    private void RemoveEmptyPanelsAndTabs()
    {
        var ribbon = AdWin.ComponentManager.Ribbon;
        foreach (var (key, source) in _panels.ToList())
        {
            if (source.Items.Count > 0)
            {
                continue;
            }

            var title = key.Split(Separator)[0];
            var tab = ribbon.Tabs.FirstOrDefault(candidate => candidate.Title == title || candidate.Id == title);
            var panel = tab?.Panels.FirstOrDefault(candidate => ReferenceEquals(candidate.Source, source));
            if (panel is not null)
            {
                tab!.Panels.Remove(panel);
            }

            _panels.Remove(key);
        }

        foreach (var title in _createdTabs.ToList())
        {
            var tab = ribbon.Tabs.FirstOrDefault(candidate => candidate.Title == title);
            if (tab is null || tab.Panels.Count > 0)
            {
                continue;
            }

            ribbon.Tabs.Remove(tab);
            _createdTabs.Remove(title);
        }
    }

    /// <summary>Everything about an extension the ribbon shows; when it is equal, nothing is rebuilt.</summary>
    private static string Signature(ExtensionDescriptor extension)
    {
        var ui = extension.Manifest.Ui!;
        var icons = ui.EffectiveButtons().Select(button => button.Icon).Where(icon => icon is not null && !icon.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase))
            .Select(icon => Path.Combine(extension.Directory, icon!))
            .Select(path => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0);
        return extension.Id + extension.Directory + ui.Tab + ui.Panel + System.Text.Json.JsonSerializer.Serialize(ui.EffectiveButtons()) + string.Join(",", icons);
    }

    private static string Coalesce(string? button, string? extension, string fallback)
    {
        return !string.IsNullOrWhiteSpace(button) ? button : !string.IsNullOrWhiteSpace(extension) ? extension : fallback;
    }

    /// <summary>The icon: <c>glyph:E8A9</c> (Segoe MDL2 Assets), a PNG in the extension folder, otherwise the first letter.</summary>
    private ImageSource LoadIcon(ExtensionDescriptor extension, ExtensionButton button)
    {
        var icon = button.Icon;
        if (!string.IsNullOrWhiteSpace(icon) && icon.StartsWith("glyph:", StringComparison.OrdinalIgnoreCase)
                                             && int.TryParse(icon["glyph:".Length..].Trim().TrimStart('U', 'u', '+'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
        {
            return Glyph(char.ConvertFromUtf32(codePoint));
        }

        if (!string.IsNullOrWhiteSpace(icon))
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(extension.Directory, icon));
                if (path.StartsWith(Path.GetFullPath(extension.Directory), StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    // OnLoad + Freeze: read now and release the file, so the folder stays editable
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or UriFormatException or ArgumentException)
            {
                LogIconFailed(logger, exception, extension.Id, icon);
            }
        }

        return Letter(string.IsNullOrWhiteSpace(button.Name) ? extension.Id : button.Name);
    }

    private static ImageSource Glyph(string glyph, int size = 32)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe MDL2 Assets"), size * 0.6875,
                new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.0);
            context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        return Render(visual, size);
    }

    private static ImageSource Letter(string label, int size = 32)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), null, new Rect(0, 0, size, size), 6, 6);
            var text = new FormattedText(label.Trim()[..1].ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.White, 1.0);
            context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        return Render(visual, size);
    }

    private static ImageSource Render(Visual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    [LoggerMessage(LogLevel.Error, "The ribbon buttons of the extensions could not be refreshed")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Extension {Id}: the icon {Icon} could not be loaded")]
    private static partial void LogIconFailed(ILogger logger, Exception exception, string id, string icon);

    private sealed record Entry(ExtensionDescriptor Extension, ExtensionButton Button, string PanelKey, int Index);

    /// <summary>The WPF command of an AdWindows button.</summary>
    private sealed class ClickCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            execute();
        }
    }
}
