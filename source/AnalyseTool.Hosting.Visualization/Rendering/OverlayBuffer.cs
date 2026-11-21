using Autodesk.Revit.DB.DirectContext3D;

namespace AnalyseTool.Hosting.Visualization.Rendering;

/// <summary>
///     Triangles or line segments of one colour in DirectContext3D buffers, ready to draw.
/// </summary>
/// <remarks>
///     The vertices carry their position only; the colour and the transparency belong to the effect. Colours per vertex
///     (<c>PositionColored</c>) are ignored by the Realistic display style: the overlay vanished in a realistic 3D view.
///     Indices are 16-bit, so the points are split into chunks of at most <see cref="ChunkSize" /> vertices, each with its
///     own buffers. Created and drawn on the Revit thread, inside <c>RenderScene</c>.
/// </remarks>
internal sealed class OverlayBuffer : IDisposable
{
    /// <summary>The most vertices in one chunk: fits 16-bit indices and is a multiple of both 2 and 3.</summary>
    public const int ChunkSize = 65_532;

    private const VertexFormatBits Format = VertexFormatBits.Position;

    private readonly PrimitiveType _type;
    private readonly VertexFormat _format = new(Format);
    private readonly EffectInstance _effect = new(Format);
    private readonly List<Chunk> _chunks = [];

    /// <param name="points">The corners of the triangles, three each, or the ends of the segments, two each.</param>
    /// <param name="type"><see cref="PrimitiveType.TriangleList" /> or <see cref="PrimitiveType.LineList" />.</param>
    /// <param name="color">The colour of the primitives.</param>
    /// <param name="transparency">Their transparency, 0 (opaque) to 1.</param>
    public OverlayBuffer(IReadOnlyList<XYZ> points, PrimitiveType type, Color color, double transparency)
    {
        _type = type;
        _effect.SetColor(color);
        _effect.SetTransparency(transparency);
        for (var start = 0; start < points.Count; start += ChunkSize)
        {
            _chunks.Add(CreateChunk(points, start, Math.Min(ChunkSize, points.Count - start)));
        }
    }

    public void Draw()
    {
        foreach (var chunk in _chunks)
        {
            DrawContext.FlushBuffer(chunk.Vertices, chunk.VertexCount, chunk.Indices, chunk.IndexCount, _format, _effect, _type, 0, chunk.Primitives);
        }
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks)
        {
            chunk.Vertices.Dispose();
            chunk.Indices.Dispose();
        }

        _chunks.Clear();
        _format.Dispose();
        _effect.Dispose();
    }

    private Chunk CreateChunk(IReadOnlyList<XYZ> points, int start, int count)
    {
        var vertexSize = count * VertexPosition.GetSizeInFloats();
        var vertices = new VertexBuffer(vertexSize);
        vertices.Map(vertexSize);
        var vertexStream = vertices.GetVertexStreamPosition();
        for (var index = start; index < start + count; index++)
        {
            vertexStream.AddVertex(new VertexPosition(points[index]));
        }

        vertices.Unmap();

        // Every primitive has its own vertices, so the indices just count up
        var triangles = _type == PrimitiveType.TriangleList;
        var primitives = count / (triangles ? 3 : 2);
        var indexSize = primitives * (triangles ? IndexTriangle.GetSizeInShortInts() : IndexLine.GetSizeInShortInts());
        var indices = new IndexBuffer(indexSize);
        indices.Map(indexSize);
        if (triangles)
        {
            var stream = indices.GetIndexStreamTriangle();
            for (var index = 0; index < primitives * 3; index += 3)
            {
                stream.AddTriangle(new IndexTriangle(index, index + 1, index + 2));
            }
        }
        else
        {
            var stream = indices.GetIndexStreamLine();
            for (var index = 0; index < primitives * 2; index += 2)
            {
                stream.AddLine(new IndexLine(index, index + 1));
            }
        }

        indices.Unmap();
        return new Chunk(vertices, count, indices, indexSize, primitives);
    }

    private sealed record Chunk(VertexBuffer Vertices, int VertexCount, IndexBuffer Indices, int IndexCount, int Primitives);
}
