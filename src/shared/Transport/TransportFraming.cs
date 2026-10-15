using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>NDJSON budgets include UTF-8 bytes, before deserialization or wire allocation.</summary>
    internal static class TransportFraming
    {
        // Keep legitimate catalogs/batches and the existing 10 MiB result contract.
        internal const int RequestBytes = 16 * 1024 * 1024;
        internal const int ResponseBytes = 32 * 1024 * 1024;
        internal const int MaxConnections = 8;
        internal static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal static async Task<string> ReadAsync(BoundedLineReader reader, int maxBytes, TimeSpan timeout)
        {
            using (var deadline = new CancellationTokenSource(timeout))
            using (deadline.Token.Register(reader.Close))
            {
                try { return await reader.ReadAsync(maxBytes, deadline.Token).ConfigureAwait(false); }
                catch (Exception) when (deadline.IsCancellationRequested)
                { throw new TimeoutException("plugin read timeout; host work already started is not cancelled. Inspect its outcome before retrying writes."); }
            }
        }

        internal static async Task WriteAsync(Stream stream, string frame, int maxBytes, TimeSpan timeout)
        {
            if (Utf8.GetByteCount(frame) > maxBytes) throw new InvalidDataException("Transport frame exceeded its byte limit.");
            var bytes = Utf8.GetBytes(frame + "\n");
            using (var deadline = new CancellationTokenSource(timeout))
            using (deadline.Token.Register(() => { try { stream.Dispose(); } catch { } }))
            {
                try
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false);
                    await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception) when (deadline.IsCancellationRequested)
                { throw new TimeoutException("plugin write timeout; delivery is unknown. Inspect host state before retrying writes."); }
            }
        }

        internal sealed class BoundedLineReader
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[4096];
            private int _offset, _length;
            internal BoundedLineReader(Stream stream) { _stream = stream; }
            internal void Close() { try { _stream.Dispose(); } catch { } }

            internal async Task<string> ReadAsync(int maxBytes, CancellationToken cancellation)
            {
                using (var frame = new MemoryStream())
                {
                    while (true)
                    {
                        if (_offset == _length)
                        {
                            _length = await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellation).ConfigureAwait(false);
                            _offset = 0;
                            if (_length == 0) return frame.Length == 0 ? null : Decode(frame);
                        }
                        var start = _offset;
                        while (_offset < _length && _buffer[_offset] != 10) _offset++;
                        var count = _offset - start;
                        if (frame.Length + count > maxBytes) throw new InvalidDataException("Transport frame exceeded its byte limit.");
                        frame.Write(_buffer, start, count);
                        if (_offset < _length) { _offset++; return Decode(frame); }
                    }
                }
            }

            private static string Decode(MemoryStream frame)
            {
                var count = checked((int)frame.Length);
                var bytes = frame.GetBuffer();
                if (count > 0 && bytes[count - 1] == 13) count--;
                var offset = count >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                return Utf8.GetString(bytes, offset, count - offset);
            }
        }
    }
}
