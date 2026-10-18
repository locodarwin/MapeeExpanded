using System.IO.Compression;

namespace WorldEditor
{
    public class GZipCompression : IKnownTypeCompression
    {
        public int Compress(ArraySlice<byte> input, ArraySlice<byte> output)
        {
            using (MemoryStream inputStream = new MemoryStream(input.Array, input.Position, input.Length))
            using (GZipStream decompressor = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                return decompressor.ReadAtLeast(output.Array.AsSpan(output.Position, output.Length), output.Length, throwOnEndOfStream: false);
            }
        }
    }
}
