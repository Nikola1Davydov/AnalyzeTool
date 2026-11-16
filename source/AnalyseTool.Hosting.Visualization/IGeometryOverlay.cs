namespace AnalyseTool.Hosting.Visualization;

/// <summary>
///     Geometry drawn over the views of a document — to show what was found or computed without touching the model.
/// </summary>
/// <remarks>
///     <para>
///         Drawn with DirectContext3D: no elements, no transaction, no undo step, nothing saved. The geometry stays until
///         <see cref="Clear" /> or until its document closes, and is drawn in the 3D views and plans of its document;
///         exported images of a view show it too. A plan presses what lies in front of its cut plane onto it, so a cut
///         element shows as its footprint.
///     </para>
///     <para>Revit thread only: call it inside a command or through <c>IRevitDispatcher</c>.</para>
/// </remarks>
/// <example>
///     <code>
///     var geometry = new OverlayGeometry(new Color(255, 0, 0), transparency: 60);
///     geometry.Add(wall);
///     overlay.Show(document, geometry);
///     </code>
/// </example>
[PublicAPI]
public interface IGeometryOverlay
{
    /// <summary>Gets how many geometries are shown now, in every document.</summary>
    int Count { get; }

    /// <summary>
    ///     Draws <paramref name="geometry" /> over the views of <paramref name="document" />, on top of what is shown already,
    ///     and redraws the active view.
    /// </summary>
    /// <param name="document">The document whose views show the geometry.</param>
    /// <param name="geometry">What to draw; it must not change afterwards.</param>
    void Show(Document document, OverlayGeometry geometry);

    /// <summary>Removes everything shown, in every document, and redraws the active view.</summary>
    /// <returns>How many geometries were removed.</returns>
    int Clear();
}
