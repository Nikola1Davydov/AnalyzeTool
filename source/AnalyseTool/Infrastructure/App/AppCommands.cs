using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AnalyseTool.Extensions.Storage;
using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;
using AnalyseTool.Hosting.Settings;
using AnalyseTool.Hosting.WebView.Controls;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace AnalyseTool.Infrastructure.App;

// The commands of the add-in's own pages: about, settings, dialogs, windows. They live in Infrastructure, not Features,
// because they show WPF dialogs and windows; none of them is open to AI clients.

/// <summary>The MCP endpoint as the settings page shows it.</summary>
/// <param name="Enabled">The user's choice; the default (null) is on in Debug, off in Release.</param>
/// <param name="State"><c>off</c>, <c>listening</c> or <c>portBusy</c>.</param>
/// <param name="Url">The address of the endpoint.</param>
/// <param name="ClaudeCode">The <c>claude mcp add …</c> command, while it listens.</param>
/// <param name="ConnectionFile">The file with the address and the token.</param>
/// <param name="Name">The server name for a client configuration, unique per Revit version.</param>
/// <param name="Token">The bearer token, while it listens: the pages build the configuration of other clients with it.</param>
public sealed record McpInfo(bool Enabled, string State, string Url, string? ClaudeCode, string ConnectionFile, string? Name = null, string? Token = null)
{
    internal static McpInfo From(IMcpEndpoint endpoint, McpOptions options)
    {
        var info = endpoint.GetInfo();
        var state = info.State switch
        {
            McpEndpointState.Listening => "listening",
            McpEndpointState.PortBusy => "portBusy",
            _ => "off"
        };
        return new McpInfo(options.Enabled ?? info.State != McpEndpointState.Off, state, info.Url.ToString(), info.ClaudeCode, info.ConnectionFile, info.Name, info.Token);
    }
}

/// <summary>What the About and Settings pages show.</summary>
/// <param name="Version">The version of AnalyseTool.</param>
/// <param name="RevitVersion">The year of the running Revit.</param>
/// <param name="Changelog">CHANGELOG.md, markdown.</param>
/// <param name="ProfileFolder"><c>%LOCALAPPDATA%\AnalyseTool</c>.</param>
/// <param name="Mcp">The MCP endpoint.</param>
public sealed record AppInfo(string Version, string RevitVersion, string Changelog, string ProfileFolder, McpInfo Mcp);

/// <summary>Returns the version, the changelog and the MCP state.</summary>
[Command(Description = "Returns the version of AnalyseTool, the Revit version, the changelog and the MCP state.")]
public sealed class GetAppInfo(IMcpEndpoint endpoint, IOptionsMonitor<McpOptions> mcp, ExtensionPaths paths, AnalyseTool.Extensions.Loading.ExtensionHost extensions) : ICommand<AppInfo>
{
    /// <inheritdoc />
    public Task<AppInfo> ExecuteAsync(CommandContext context)
    {
        var assembly = typeof(GetAppInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                      ?? assembly.GetName().Version?.ToString() ?? "";
        var changelog = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "CHANGELOG.md");
        return Task.FromResult(new AppInfo(
            version,
            extensions.RevitVersion,
            File.Exists(changelog) ? File.ReadAllText(changelog) : "",
            paths.ProfileRoot,
            McpInfo.From(endpoint, mcp.CurrentValue)));
    }
}

/// <summary>The colour theme of Revit.</summary>
/// <param name="Theme"><c>light</c> or <c>dark</c>.</param>
public sealed record RevitTheme(string Theme);

/// <summary>
///     The theme Revit shows (Revit 2024+ has a dark one): the pages follow it when the profile says «Revit». WebView2 only
///     knows the theme of Windows, so the pages ask.
/// </summary>
[Command(Description = "Returns the colour theme of Revit: light or dark.")]
public sealed class GetRevitTheme : ICommand<RevitTheme>
{
    /// <inheritdoc />
    public Task<RevitTheme> ExecuteAsync(CommandContext context)
    {
        try
        {
            return Task.FromResult(new RevitTheme(Autodesk.Revit.UI.UIThemeManager.CurrentTheme == Autodesk.Revit.UI.UITheme.Dark ? "dark" : "light"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or TypeLoadException or MissingMethodException)
        {
            // Outside Revit (tests) or an API without themes: light, as Revit itself
            return Task.FromResult(new RevitTheme("light"));
        }
    }
}

/// <summary>Input of <see cref="SetMcpEnabled" />.</summary>
public sealed record SetMcpEnabledRequest([property: Description("Whether AI clients may connect.")] bool Enabled);

/// <summary>Turns the MCP endpoint on or off for this user; takes effect at the next start of Revit.</summary>
[Command(Description = "Turns the MCP endpoint on or off for this user (Mcp:Enabled in the user settings). Takes effect at the next start of Revit.")]
public sealed class SetMcpEnabled(IUserSettings settings, IMcpEndpoint endpoint, IOptionsMonitor<McpOptions> mcp) : ICommand<SetMcpEnabledRequest, McpInfo>
{
    /// <inheritdoc />
    public async Task<McpInfo> ExecuteAsync(SetMcpEnabledRequest request, CommandContext context)
    {
        await settings.UpdateAsync<McpOptions>(McpOptions.SectionName, options => options.Enabled = request.Enabled, context.CancellationToken);
        return McpInfo.From(endpoint, mcp.CurrentValue) with { Enabled = request.Enabled };
    }
}

/// <summary>Input of <see cref="OpenPath" />.</summary>
public sealed record OpenPathRequest([property: Description("A folder or a file.")] string Path);

/// <summary>Opens a folder in Explorer or a file with its program.</summary>
[Command(Description = "Opens a folder in Explorer, or a file with the program Windows associates with it.")]
public sealed class OpenPath : ICommand<OpenPathRequest, Empty>
{
    /// <inheritdoc />
    public Task<Empty> ExecuteAsync(OpenPathRequest request, CommandContext context)
    {
        var path = Path.GetFullPath(request.Path);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new CommandException(CommandErrorCodes.NotFound, $"{path} does not exist.");
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.FromResult(Empty.Value);
    }
}

/// <summary>The path the user picked, or <see langword="null" /> when they cancelled.</summary>
/// <param name="Path">The full path.</param>
public sealed record BrowseResult(string? Path);

/// <summary>Input of <see cref="BrowseFolder" />.</summary>
public sealed record BrowseFolderRequest(
    [property: Description("The title of the dialog.")] string? Title = null,
    [property: Description("The folder it starts in.")] string? Initial = null);

/// <summary>Lets the user pick a folder.</summary>
[Command(Description = "Shows the folder picker of Windows.")]
public sealed class BrowseFolder : ICommand<BrowseFolderRequest, BrowseResult>
{
    /// <inheritdoc />
    public Task<BrowseResult> ExecuteAsync(BrowseFolderRequest request, CommandContext context)
    {
        return Ui.InvokeAsync(() =>
        {
            var dialog = new OpenFolderDialog { Title = request.Title ?? "Choose a folder" };
            if (!string.IsNullOrWhiteSpace(request.Initial) && Directory.Exists(request.Initial))
            {
                dialog.InitialDirectory = request.Initial;
            }

            return new BrowseResult(dialog.ShowDialog(Ui.Owner) == true ? dialog.FolderName : null);
        });
    }
}

/// <summary>Input of <see cref="BrowseFile" />.</summary>
public sealed record BrowseFileRequest(
    [property: Description("The title of the dialog.")] string? Title = null,
    [property: Description("A filter as Windows writes it, e.g. \"Shared parameters|*.txt|All files|*.*\".")] string? Filter = null,
    [property: Description("A save dialog instead of an open one.")] bool Save = false,
    [property: Description("The file or folder it starts at.")] string? Initial = null);

/// <summary>Lets the user pick a file.</summary>
[Command(Description = "Shows the open or save file dialog of Windows.")]
public sealed class BrowseFile : ICommand<BrowseFileRequest, BrowseResult>
{
    /// <inheritdoc />
    public Task<BrowseResult> ExecuteAsync(BrowseFileRequest request, CommandContext context)
    {
        return Ui.InvokeAsync(() =>
        {
            FileDialog dialog = request.Save ? new SaveFileDialog() : new OpenFileDialog();
            dialog.Title = request.Title ?? (request.Save ? "Save as" : "Open");
            dialog.Filter = string.IsNullOrWhiteSpace(request.Filter) ? "All files|*.*" : request.Filter;
            if (!string.IsNullOrWhiteSpace(request.Initial))
            {
                if (Directory.Exists(request.Initial))
                {
                    dialog.InitialDirectory = request.Initial;
                }
                else
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(request.Initial);
                    dialog.FileName = Path.GetFileName(request.Initial);
                }
            }

            return new BrowseResult(dialog.ShowDialog(Ui.Owner) == true ? dialog.FileName : null);
        });
    }
}

/// <summary>Input of <see cref="OpenWindow" />.</summary>
public sealed record OpenWindowRequest(
    [property: Description("The route of the page, e.g. /extensions/count-walls.")] string Route,
    [property: Description("Show it in the dock pane instead of a window.")] bool Dock = false);

/// <summary>Opens a page of AnalyseTool in its own window or the dock pane — from a ribbon button or the dock pane, for example.</summary>
[Command(Description = "Opens a page of AnalyseTool in its own window, or in the dock pane.")]
public sealed class OpenWindow(IWindows windows) : ICommand<OpenWindowRequest, Empty>
{
    /// <inheritdoc />
    public async Task<Empty> ExecuteAsync(OpenWindowRequest request, CommandContext context)
    {
        await Ui.InvokeAsync(() =>
        {
            if (request.Dock)
            {
                windows.ShowDockPane(request.Route);
            }
            else
            {
                AppWindows.Open(windows, request.Route);
            }

            return true;
        });
        return Empty.Value;
    }
}

/// <summary>Input of <see cref="OpenUrl" />.</summary>
public sealed record OpenUrlRequest([property: Description("An http or https address.")] string Url);

/// <summary>Opens a web address in the default browser.</summary>
[Command(Description = "Opens an http or https address in the default browser.")]
public sealed class OpenUrl : ICommand<OpenUrlRequest, Empty>
{
    /// <inheritdoc />
    public Task<Empty> ExecuteAsync(OpenUrlRequest request, CommandContext context)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, $"Not a web address: {request.Url}");
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.FromResult(Empty.Value);
    }
}

/// <summary>The windows of the add-in's pages, with their titles and sizes.</summary>
public static class AppWindows
{
    /// <summary>Opens a route in a window titled after the page.</summary>
    public static void Open(IWindows windows, string route)
    {
        var (title, width, height) = route switch
        {
            "/" or "" => ("AnalyseTool", 1280d, 820d),
            "/extensions" => ("AnalyseTool — Manage buttons", 1000d, 720d),
            "/extensions/new" => ("AnalyseTool — New button", 560d, 720d),
            "/settings" => ("AnalyseTool — Settings", 720d, 780d),
            "/about" => ("AnalyseTool — About", 640d, 760d),
            "/statistics" => ("AnalyseTool — Statistics", 960d, 760d),
            _ when route.StartsWith("/extensions/", StringComparison.Ordinal) => ($"AnalyseTool — {route["/extensions/".Length..]}", 1200d, 800d),
            _ => ("AnalyseTool", 1000d, 700d)
        };
        windows.Open(route, options =>
        {
            options.Title = title;
            options.Width = width;
            options.Height = height;
        });
    }
}

/// <summary>The WPF thread of Revit, for dialogs and windows asked for by a command on the thread pool.</summary>
internal static class Ui
{
    public static Window? Owner => System.Windows.Application.Current?.MainWindow;

    public static Task<T> InvokeAsync<T>(Func<T> work)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher
                         ?? throw new CommandException(CommandErrorCodes.Failed, "The user interface of Revit is not available.");
        return dispatcher.InvokeAsync(work).Task;
    }
}
