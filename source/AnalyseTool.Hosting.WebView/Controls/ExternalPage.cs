namespace AnalyseTool.Hosting.WebView.Controls;

/// <summary>
///     A page that is not a route of ClientApp: an HTML file of a folder (served through a virtual host) or a dev server —
///     for example the page of a user's extension.
/// </summary>
/// <param name="Key">Identifies the page: a second <c>OpenPage</c> with the same key brings the open window back.</param>
/// <param name="Url">What to navigate to: <c>https://&lt;HostName&gt;/&lt;file&gt;</c> or a dev server.</param>
/// <remarks>
///     The page talks to the pipeline over the same WebView2 messages as ClientApp; <see cref="InitScript" /> runs before
///     the page's own scripts and can give it a friendlier API over them.
/// </remarks>
[PublicAPI]
public sealed record ExternalPage(string Key, string Url)
{
    /// <summary>Gets the virtual host name the folder is served under, e.g. <c>acme.ext.analysetool.local</c>.</summary>
    public string? HostName { get; init; }

    /// <summary>
    ///     The script for the page shown as a frame inside another page (the dock pane): run in every frame of that WebView, it
    ///     must check where it runs.
    /// </summary>
    public string? FrameScript { get; init; }

    /// <summary>Gets the folder served under <see cref="HostName" />.</summary>
    public string? Folder { get; init; }

    /// <summary>Gets a script that runs in every document of the page before its own scripts.</summary>
    public string? InitScript { get; init; }
}
