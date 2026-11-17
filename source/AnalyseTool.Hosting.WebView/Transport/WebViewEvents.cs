namespace AnalyseTool.Hosting.WebView.Transport;

/// <summary>
///     Tells every open page that something changed in the host — instead of the pages asking again every few seconds.
///     A page listens with <c>bridge.on(name, listener)</c> of revit-bridge.
/// </summary>
[PublicAPI]
public interface IWebViewEvents
{
    /// <summary>Sends <c>{ type: "event", name, payload }</c> to every open page; from any thread.</summary>
    /// <param name="name">The event, for example <c>revitThemeChanged</c>.</param>
    /// <param name="payload">What the pages get, serialized as the results of commands; <see langword="null" /> for none.</param>
    void Publish(string name, object? payload = null);
}

/// <summary>The open pages subscribe with their transport while they live.</summary>
internal sealed class WebViewEvents : IWebViewEvents
{
    /// <summary>Raised by <see cref="Publish" />, on the thread of the caller.</summary>
    public event Action<string, object?>? Published;

    /// <inheritdoc />
    public void Publish(string name, object? payload = null)
    {
        Published?.Invoke(name, payload);
    }
}
