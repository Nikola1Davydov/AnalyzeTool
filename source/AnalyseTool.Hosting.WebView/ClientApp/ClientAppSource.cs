using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;

namespace AnalyseTool.Hosting.WebView.ClientApp;

/// <summary>
///     The <c>ClientApp</c> configuration section: where the pages come from.
/// </summary>
[PublicAPI]
public sealed class ClientAppOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ClientApp";

    /// <summary>Gets or sets the Vite dev server, used in Development.</summary>
    public string DevServerUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    ///     Gets or sets a value indicating whether to load the dev server; <see langword="null" /> — only in Development.
    ///     Set <see langword="false" /> in Development to try the built <c>dist</c>.
    /// </summary>
    public bool? UseDevServer { get; set; }

    /// <summary>Gets or sets the built pages, relative to the add-in folder.</summary>
    public string DistPath { get; set; } = Path.Combine("ClientApp", "dist");
}

/// <summary>
///     Decides how every window and the dock pane load the pages, and which origin may talk to the pipeline.
/// </summary>
/// <remarks>
///     Release serves <c>dist</c> over a private virtual host (<c>https://app.&lt;add-in&gt;.local</c>); Development loads
///     the Vite dev server with HMR. Pages use hash routes (<c>#/route</c>), so no server-side routing is needed.
/// </remarks>
internal sealed class ClientAppSource
{
    public ClientAppSource(IOptions<ClientAppOptions> options, IHostEnvironment environment)
    {
        var value = options.Value;
        UseDevServer = value.UseDevServer ?? environment.IsDevelopment();
        HostName = $"app.{Sanitize(environment.ApplicationName)}.local";
        DistFolder = Path.Combine(environment.ContentRootPath, value.DistPath);
        Origin = UseDevServer ? new Uri(value.DevServerUrl).GetLeftPart(UriPartial.Authority) : $"https://{HostName}";
    }

    /// <summary>Gets a value indicating whether the pages come from the dev server.</summary>
    public bool UseDevServer { get; }

    /// <summary>Gets the virtual host of the built pages.</summary>
    public string HostName { get; }

    /// <summary>Gets the folder of the built pages.</summary>
    public string DistFolder { get; }

    /// <summary>Gets the only origin allowed to load in the WebView and to send commands.</summary>
    public string Origin { get; }

    /// <summary>Maps the virtual host to <see cref="DistFolder" /> unless the dev server is used.</summary>
    public void Map(CoreWebView2 core)
    {
        if (!UseDevServer)
        {
            core.SetVirtualHostNameToFolderMapping(HostName, DistFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        }
    }

    /// <summary>Returns the address of a page route, for example <c>/</c> or <c>/families</c>.</summary>
    public string GetUrl(string route)
    {
        var hash = "#" + (route.StartsWith('/') ? route : "/" + route);
        return UseDevServer ? $"{Origin}/{hash}" : $"{Origin}/index.html{hash}";
    }

    /// <summary>Returns whether the address belongs to <see cref="Origin" />.</summary>
    public bool IsOwn(string? uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
               && string.Equals(parsed.GetLeftPart(UriPartial.Authority), Origin, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string name)
    {
        var chars = name.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-').ToArray();
        return new string(chars).Trim('-');
    }
}
