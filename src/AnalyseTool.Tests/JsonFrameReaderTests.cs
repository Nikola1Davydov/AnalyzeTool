using AnalyseTool.Mcp;
using System.Text;

namespace AnalyseTool.Tests;

/// <summary>The bridge wire's framing, shared by both ends. The bridge's own copy used to drop whatever
/// followed the first value in a read, which broke two requests sent back to back on one connection.</summary>
public class JsonFrameReaderTests
{
    /// <summary>Hands out the given chunks one read at a time, like a socket that delivers in pieces.</summary>
    private sealed class ChunkedStream(params string[] chunks) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks.Select(Encoding.UTF8.GetBytes));

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_chunks.Count == 0) return 0;
            byte[] next = _chunks.Dequeue();
            if (next.Length > count) throw new InvalidOperationException("test chunk larger than the read buffer");
            next.CopyTo(buffer, offset);
            return next.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task Two_values_in_one_read_come_out_as_two()
    {
        JsonFrameReader reader = new(new ChunkedStream("{\"id\":\"a\"}{\"id\":\"b\"}"), 1024);

        using (Assert.Multiple())
        {
            await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo("{\"id\":\"a\"}");
            await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo("{\"id\":\"b\"}");
            await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsNull();
        }
    }

    [Test]
    public async Task A_value_split_across_reads_comes_out_whole()
    {
        JsonFrameReader reader = new(new ChunkedStream("{\"payload\":{\"a\"", ":[1,2", "]}}{\"next\":1}"), 1024);

        await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo("{\"payload\":{\"a\":[1,2]}}");
        await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo("{\"next\":1}");
    }

    [Test]
    public async Task Brackets_and_escaped_quotes_inside_strings_do_not_end_a_value()
    {
        const string value = "{\"text\":\"a } ] \\\" { [\"}";
        JsonFrameReader reader = new(new ChunkedStream(value), 1024);

        await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo(value);
    }

    [Test]
    public async Task A_stream_that_ends_mid_value_returns_what_arrived()
    {
        JsonFrameReader reader = new(new ChunkedStream("{\"id\":"), 1024);

        await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsEqualTo("{\"id\":");
        await Assert.That(await reader.ReadFrameAsync(CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task A_value_over_the_limit_is_refused()
    {
        JsonFrameReader reader = new(new ChunkedStream("{\"big\":\"" + new string('x', 200) + "\"}"), 100);

        await Assert.That(async () => await reader.ReadFrameAsync(CancellationToken.None)).Throws<InvalidDataException>();
    }
}
