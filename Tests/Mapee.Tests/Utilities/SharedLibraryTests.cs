using CommonUtilities.Data;
using Mapper;
using WorldEditor;

namespace Mapee.Tests.Utilities
{
    // The pieces swapped to make the libraries run on Linux, checked against data made by other implementations
    // (Python's zlib, gzip and a hand-built PNG) so they aren't only checked against .NET itself
    public class SharedLibraryTests
    {
        private const string Text = "Mapee reads this ";
        private const string ZLibData = "eNrzTSxITVUoSk1MKVYoycgsVvAdFRgVGHwCAIrS8lk=";
        private const string GZipData = "H4sIAAAAAAAC//NNLEhNVShKTUwpVijJyCxW8B0VGBUYfAIAW664jqgCAAA=";

        // 2x2: red, green / blue, half-transparent white
        private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAE0lEQVR42mP4z8DwHwyBNAg0AABJSQl4nFEXkgAAAABJRU5ErkJggg==";

        [Theory]
        [InlineData(ZLibData, CompressionType.ZLib)]
        [InlineData(GZipData, CompressionType.GZip)]
        public void DecompressesChunkData(string data, CompressionType type)
        {
            byte[] input = Convert.FromBase64String(data);
            byte[] output = new byte[4096];

            int length = new Compression().Compress(new ArraySlice<byte>(input), new ArraySlice<byte>(output), type);

            Assert.Equal(string.Concat(Enumerable.Repeat(Text, 40)), System.Text.Encoding.ASCII.GetString(output, 0, length));
        }

        [Fact]
        public void DecodesPngIntoBgra()
        {
            using MemoryStream stream = new(Convert.FromBase64String(Png));
            ReadOnlyBitmap bitmap = new(stream);

            Assert.Equal(new System.Drawing.Size(2, 2), bitmap.Size);
            Assert.Equal(System.Drawing.Color.FromArgb(255, 255, 0, 0), bitmap.GetPixel(0, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 255, 0), bitmap.GetPixel(1, 0));
            Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 0, 255), bitmap.GetPixel(0, 1));
            Assert.Equal(System.Drawing.Color.FromArgb(128, 255, 255, 255), bitmap.GetPixel(1, 1));
            Assert.Equal([0, 0, 255, 255], bitmap.PixelData[..4]); // red as blue, green, red, alpha
        }

        [Fact]
        public void ReadsTheColourmapsStylesUse()
        {
            ReadOnlyBitmap grass = new(Path.Combine("Resources", "Mapper", "Textured", "colormap", "grass.png"));
            Assert.Equal(new System.Drawing.Size(256, 256), grass.Size);
        }

        [Theory]
        [InlineData("#F80", 255, 255, 136, 0)]
        [InlineData("#8F80", 136, 255, 136, 0)]
        [InlineData("#336699", 255, 0x33, 0x66, 0x99)]
        [InlineData("#80336699", 0x80, 0x33, 0x66, 0x99)]
        [InlineData("Red", 255, 255, 0, 0)]
        public void ParsesStyleColours(string text, int a, int r, int g, int b)
        {
            System.Drawing.Color colour = ColorParser.Parse(text);
            Assert.Equal((a, r, g, b), ((int)colour.A, (int)colour.R, (int)colour.G, (int)colour.B));
        }

        [Theory]
        [InlineData("#12")]
        [InlineData("#GGGGGG")]
        [InlineData("NotAColour")]
        public void RejectsBadColours(string text)
        {
            Assert.ThrowsAny<FormatException>(() => ColorParser.Parse(text));
        }

        [Fact]
        public void NormalisesEitherSlash()
        {
            Assert.Equal(Path.Combine("Settings", "Biome", "Colors.json"), DataPath.Normalize("Settings\\Biome/Colors.json"));
        }
    }
}
