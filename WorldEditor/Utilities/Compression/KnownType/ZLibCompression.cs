using System.IO.Compression;

namespace WorldEditor
{
    public class ZLibCompression : IKnownTypeCompression
    {
        public int Compress(ArraySlice<byte> input, ArraySlice<byte> output)
        {
            using (MemoryStream inputStream = new MemoryStream(input.Array, input.Position, input.Length))
            using (ZLibStream decompressor = new ZLibStream(inputStream, CompressionMode.Decompress))
            {
                return decompressor.ReadAtLeast(output.Array.AsSpan(output.Position, output.Length), output.Length, throwOnEndOfStream: false);
            }
        }
    }
}
