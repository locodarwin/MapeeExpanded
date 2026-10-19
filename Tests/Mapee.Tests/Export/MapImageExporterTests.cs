using Mapper;
using StbImageSharp;
using System.Buffers.Binary;
using WorldEditor;

namespace Mapee.Tests.Export
{
    // The pieces of the one-image export (mapee-cli render) that don't need a world drawn: the PNG written a row at
    // a time and the area option
    public class MapImageExporterTests
    {
        [Fact]
        public void RowByRowPngsReadBackExactlyAcrossSeveralDataChunks()
        {
            // Random pixels hardly compress, so 1.4 MB of them needs more than one IDAT chunk
            const int width = 700, height = 500;
            byte[] rgba = new byte[width * height * 4];
            new Random(9).NextBytes(rgba);

            using MemoryStream stream = new();
            using (PngRowWriter writer = new(stream, width, height))
            {
                for (int y = 0; y < height; y++) writer.WriteRow(rgba.AsSpan(y * width * 4, width * 4));
            }

            byte[] png = stream.ToArray();
            ImageResult image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(rgba, image.Data);
            Assert.True(ChunkTypes(png).Count(type => type == "IDAT") > 1);
            Assert.Equal("IEND", ChunkTypes(png).Last());
        }

        [Fact]
        public void APngMissingRowsIsAnError()
        {
            using MemoryStream stream = new();
            PngRowWriter writer = new(stream, 4, 3);
            writer.WriteRow(new byte[16]);

            Assert.Throws<ArgumentException>(() => writer.WriteRow(new byte[12]));
            Assert.Throws<InvalidOperationException>(writer.Dispose);
        }

        [Fact]
        public void AnAreaTakesItsCornersInEitherOrder()
        {
            Assert.True(BlockArea.TryParse("500, -20,-500,20", out BlockArea area));
            Assert.Equal((-500, -20, 500, 20), (area.MinX, area.MinZ, area.MaxX, area.MaxZ));

            Assert.False(BlockArea.TryParse("1,2,3", out _));
            Assert.False(BlockArea.TryParse("1,2,3,four", out _));
            Assert.False(BlockArea.TryParse("1.5,2,3,4", out _));
        }

        [Fact]
        public void AnAreaTouchesTheRegionsItOverlaps()
        {
            BlockArea area = new(0, 0, 511, 511);
            Assert.True(area.TouchesRegion(new Coords(0, 0)));
            Assert.False(area.TouchesRegion(new Coords(1, 0)));
            Assert.False(area.TouchesRegion(new Coords(-1, 0)));

            BlockArea across = new(-1, -1, 512, 0);
            Assert.True(across.TouchesRegion(new Coords(-1, -1)));
            Assert.True(across.TouchesRegion(new Coords(1, 0)));
            Assert.False(across.TouchesRegion(new Coords(2, 0)));
            Assert.False(across.TouchesRegion(new Coords(0, 1)));
        }

        private static List<string> ChunkTypes(byte[] png)
        {
            List<string> types = [];
            for (int i = 8; i < png.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(i));
                types.Add(System.Text.Encoding.ASCII.GetString(png, i + 4, 4));
                i += 12 + length;
            }
            return types;
        }
    }
}
