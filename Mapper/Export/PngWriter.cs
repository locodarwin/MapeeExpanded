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
            output.Write(Signature);

            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8;  // bits per channel
            header[9] = 6;  // RGBA
            WriteChunk(output, "IHDR", header);

            using (MemoryStream compressed = new())
            {
                using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
                {
                    WriteRows(zlib, rgba, width, height);
                }
                WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
            }

            WriteChunk(output, "IEND", []);
        }

        private static void WriteRows(Stream output, byte[] rgba, int width, int height)
        {
            int stride = width * 4;
            byte[] best = new byte[stride + 1], trial = new byte[stride + 1];

            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = rgba.AsSpan(y * stride, stride);
                ReadOnlySpan<byte> above = y == 0 ? new byte[stride] : rgba.AsSpan((y - 1) * stride, stride);

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

        private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
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
}
