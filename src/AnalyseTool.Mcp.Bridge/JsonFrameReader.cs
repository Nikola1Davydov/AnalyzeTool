using System.IO;
using System.Text;

namespace AnalyseTool.Mcp
{
    /// <summary>
    /// Splits a stream into top-level JSON values — the framing of the bridge wire (see McpWire), read by
    /// BOTH ends: the bridge reads requests with it, the exe reads progress frames and replies. Linked
    /// into the exe like McpWire.cs, so the two ends cannot drift: they used to keep a copy each, and the
    /// bridge's had already lost whatever followed the first value in a read.
    ///
    /// Nesting depth is tracked across reads (strings and escapes honoured), so a boundary is found in
    /// O(bytes) without parsing anything twice, and the bytes after a value are kept for the next one —
    /// two frames in one read, or one frame across several, both come out whole. This answers "where
    /// does the value end", not "is it valid": parsing still decides that.
    /// </summary>
    internal sealed class JsonFrameReader
    {
        private readonly Stream _stream;
        private readonly int _maxFrameBytes;
        private readonly MemoryStream _pending = new();
        private readonly byte[] _buffer = new byte[8192];
        private int _scanned;   // bytes of _pending already scanned
        private int _depth;
        private bool _inString;
        private bool _escaped;

        public JsonFrameReader(Stream stream, int maxFrameBytes)
        {
            _stream = stream;
            _maxFrameBytes = maxFrameBytes;
        }

        /// <summary>The next value as text; null at end of stream with nothing pending. At end of stream
        /// mid-value, what arrived is returned and the parse says what is wrong. A value over the size
        /// limit throws <see cref="InvalidDataException"/>.</summary>
        public async Task<string?> ReadFrameAsync(CancellationToken ct)
        {
            while (true)
            {
                int end = ScanForFrameEnd();
                if (end > 0)
                {
                    string frame = Encoding.UTF8.GetString(_pending.GetBuffer(), 0, end);
                    // Keep what followed the frame for the next call.
                    int rest = (int)_pending.Length - end;
                    byte[] tail = rest > 0 ? _pending.GetBuffer().AsSpan(end, rest).ToArray() : Array.Empty<byte>();
                    _pending.SetLength(0);
                    if (tail.Length > 0) _pending.Write(tail, 0, tail.Length);
                    _scanned = 0;
                    _depth = 0;
                    return frame;
                }

                int read = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    if (_pending.Length == 0) return null;
                    string partial = Encoding.UTF8.GetString(_pending.GetBuffer(), 0, (int)_pending.Length);
                    _pending.SetLength(0);
                    _scanned = 0;
                    return partial;
                }
                _pending.Write(_buffer, 0, read);
                if (_pending.Length > _maxFrameBytes)
                    throw new InvalidDataException($"A message exceeded {_maxFrameBytes / (1024 * 1024)} MB.");
            }
        }

        /// <summary>Offset just past the first complete top-level value in the pending bytes, or 0.
        /// Whitespace between values is skipped as part of the next one; the protocol only sends
        /// objects, so a value is complete when its outermost bracket closes.</summary>
        private int ScanForFrameEnd()
        {
            byte[] bytes = _pending.GetBuffer();
            int length = (int)_pending.Length;
            for (; _scanned < length; _scanned++)
            {
                char c = (char)bytes[_scanned];
                if (_inString)
                {
                    if (_escaped) _escaped = false;
                    else if (c == '\\') _escaped = true;
                    else if (c == '"') _inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': _inString = true; break;
                    case '{':
                    case '[': _depth++; break;
                    case '}':
                    case ']':
                        if (--_depth <= 0)
                        {
                            _depth = 0;
                            return ++_scanned;
                        }
                        break;
                }
            }
            return 0;
        }
    }
}
