using System.Buffers.Binary;
using System.IO.Compression;

namespace Mapper
{
    // Writes RGBA images as PNG without an imaging library, so tiles can be made on any platform and off the
    // UI thread. Each row is filtered whichever way leaves the smallest numbers, as most encoders do
    public static class PngWriter
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static void Write(string path, byte[] rgba, int width, int height)
        {
            using FileStream file = File.Create(path);
            Write(file, rgba, width, height);
        }

        public static void Write(Stream output, byte[] rgba, int width, int height)
        {
            using PngRowWriter writer = new(output, width, height);
            for (int y = 0; y < height; y++) writer.WriteRow(rgba.AsSpan(y * width * 4, width * 4));
        }

        internal static void WriteHeader(Stream output, int width, int height)
        {
            output.Write(Signature);

            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8;  // bits per channel
            header[9] = 6;  // RGBA
            WriteChunk(output, "IHDR", header);
        }

        // Writes the row, filtered whichever way leaves the smallest numbers, with its filter byte first
        internal static void WriteRow(Stream output, ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, ref byte[] best, ref byte[] trial)
        {
            long bestScore = long.MaxValue;
            for (byte filter = 0; filter <= 4; filter++)
            {
                long score = Filter(filter, row, above, trial);
                if (score >= bestScore) continue;

                bestScore = score;
                (best, trial) = (trial, best);
            }
            output.Write(best);
        }

        // Fills output with the filter byte and the filtered row; returns how far the bytes are from zero
        private static long Filter(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, byte[] output)
        {
            output[0] = filter;
            long score = 0;

            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= 4 ? row[i - 4] : 0;
                int up = above[i];
                int upLeft = i >= 4 ? above[i - 4] : 0;

                int predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0
                };

                byte value = (byte)(row[i] - predicted);
                output[i + 1] = value;
                score += value < 128 ? value : 256 - value;
            }
            return score;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        internal static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);

            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);

            uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
            BinaryPrimitives.WriteUInt32BigEndian(number, crc);
            output.Write(number);
        }

        private static uint Crc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] CreateCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }

    // Writes a PNG a row at a time, so an image too big to hold in memory can be written as it's drawn. The
    // compressed data goes out in IDAT chunks of up to a megabyte. Disposing writes the end of the file
    public sealed class PngRowWriter : IDisposable
    {
        private readonly Stream _output;
        private readonly int _width, _height;
        private readonly IdatStream _idat;
        private readonly ZLibStream _zlib;
        private byte[] _best, _trial;
        private readonly byte[] _above;  // zeros before the first row, as the filters expect
        private int _rows;
        private bool _disposed;

        public PngRowWriter(Stream output, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A PNG needs at least one pixel");

            _output = output;
            _width = width;
            _height = height;
            _above = new byte[width * 4];
            _best = new byte[width * 4 + 1];
            _trial = new byte[width * 4 + 1];

            PngWriter.WriteHeader(output, width, height);
            _idat = new IdatStream(output);
            _zlib = new ZLibStream(_idat, CompressionLevel.Optimal, leaveOpen: true);
        }

        // One row of RGBA pixels, top first
        public void WriteRow(ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length != _width * 4) throw new ArgumentException($"A row is {_width * 4} bytes", nameof(rgba));
            if (_rows == _height) throw new InvalidOperationException("Every row has been written");

            PngWriter.WriteRow(_zlib, rgba, _above, ref _best, ref _trial);
            rgba.CopyTo(_above);
            _rows++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _zlib.Dispose();
            _idat.Flush();
            if (_rows != _height) throw new InvalidOperationException($"Only {_rows} of {_height} rows were written");
            PngWriter.WriteChunk(_output, "IEND", []);
        }

        private sealed class IdatStream(Stream output) : Stream
        {
            private const int ChunkSize = 1 << 20;
            private readonly MemoryStream _buffer = new();

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _buffer.Write(buffer);
                if (_buffer.Length >= ChunkSize) Flush();
            }

            public override void Flush()
            {
                if (_buffer.Length == 0) return;
                PngWriter.WriteChunk(output, "IDAT", _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length));
                _buffer.SetLength(0);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
