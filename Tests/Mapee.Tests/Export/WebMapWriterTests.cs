using Mapper;
using StbImageSharp;
using WorldEditor;

namespace Mapee.Tests.Export
{
    // The web map export's tiles, zoom levels and page, from regions drawn by hand
    public class WebMapWriterTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-webmap-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private static byte[] Solid(int width, int height, byte b, byte g, byte r)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
            return pixels;
        }

        private static ImageResult ReadTile(string path) => ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

        private static byte[] Pixel(ImageResult image, int x, int y) => image.Data.AsSpan((y * image.Width + x) * 4, 4).ToArray();

        [Fact]
        public void PngsReadBackExactly()
        {
            const int width = 37, height = 23;
            byte[] rgba = new byte[width * height * 4];
            Random random = new(5);
            random.NextBytes(rgba);

            using MemoryStream stream = new();
            PngWriter.Write(stream, rgba, width, height);
            ImageResult image = ImageResult.FromMemory(stream.ToArray(), ColorComponents.RedGreenBlueAlpha);

            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(rgba, image.Data);
        }

        [Fact]
        public void RegionsBecomeTilesAndZoomLevels()
        {
            WebMapWriter writer = new(_folder);
            Coords[] regions = [new(0, 0), new(1, 0), new(-1, -1), new(5, 5), new(6, 5), new(-8, 3)];
            foreach (Coords region in regions)
            {
                writer.AddRegion(region, region.X * 512, region.Z * 512, 512, 512, Solid(512, 512, 0, 0, 255));
            }
            writer.Finish(new WebMapInfo() { Title = "Tom & Jerry's", Markers = [new WebMapMarker("Spawn", 10, -20)] });

            foreach (Coords region in regions) Assert.True(File.Exists(Path.Combine(_folder, "tiles", "0", $"{region.X}_{region.Z}.png")));

            // Six regions halve to five tiles, then four, where it stops
            Assert.Equal(5, Directory.GetFiles(Path.Combine(_folder, "tiles", "-1")).Length);
            Assert.Equal(4, Directory.GetFiles(Path.Combine(_folder, "tiles", "-2")).Length);
            Assert.False(Directory.Exists(Path.Combine(_folder, "tiles", "-3")));

            // Region (0, 0) fills the top left quarter of tile (0, 0) below it; nothing is under the bottom half
            ImageResult parent = ReadTile(Path.Combine(_folder, "tiles", "-1", "0_0.png"));
            Assert.Equal(512, parent.Width);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(parent, 10, 10));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(parent, 300, 10));
            Assert.Equal(0, Pixel(parent, 10, 300)[3]);

            string page = File.ReadAllText(Path.Combine(_folder, "index.html"));
            Assert.Contains(WebMapWriter.Marker, page);
            Assert.Contains("<title>Tom &amp; Jerry&#39;s</title>", page);
            Assert.Contains("\"minLevel\":-2", page);
            Assert.Contains("\"name\":\"Spawn\"", page);
            Assert.DoesNotContain("{{DATA}}", page);
        }

        [Fact]
        public void PartRegionsLandWhereTheirBlocksAre()
        {
            WebMapWriter writer = new(_folder);

            // Blocks x -300 to -201, z -512 to -503: region (-1, -1), from pixel (212, 0)
            writer.AddRegion(new Coords(-1, -1), -300, -512, 100, 10, Solid(100, 10, 255, 0, 0));
            writer.Finish(new WebMapInfo());

            ImageResult tile = ReadTile(Path.Combine(_folder, "tiles", "0", "-1_-1.png"));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(tile, 212, 0));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(tile, 311, 9));
            Assert.Equal(0, Pixel(tile, 211, 0)[3]);
            Assert.Equal(0, Pixel(tile, 312, 0)[3]);
            Assert.Equal(0, Pixel(tile, 212, 10)[3]);
        }

        [Fact]
        public void EmptyRegionsWriteNoTile()
        {
            WebMapWriter writer = new(_folder);
            writer.AddRegion(new Coords(0, 0), 0, 0, 512, 512, new byte[512 * 512 * 4]);
            writer.Finish(new WebMapInfo());

            Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "tiles", "0")));
            Assert.Contains("\"regions\":[]", File.ReadAllText(Path.Combine(_folder, "index.html")));
        }

        [Fact]
        public void RefusesAFolderHoldingSomethingElse()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "mine");

            Assert.Throws<IOException>(() => new WebMapWriter(_folder));
            Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
        }

        [Fact]
        public void ReplacesAnEarlierExport()
        {
            WebMapWriter first = new(_folder);
            first.AddRegion(new Coords(3, 3), 1536, 1536, 512, 512, Solid(512, 512, 0, 255, 0));
            first.Finish(new WebMapInfo());

            WebMapWriter second = new(_folder);
            second.AddRegion(new Coords(0, 0), 0, 0, 512, 512, Solid(512, 512, 0, 255, 0));
            second.Finish(new WebMapInfo());

            Assert.False(File.Exists(Path.Combine(_folder, "tiles", "0", "3_3.png")));
            Assert.True(File.Exists(Path.Combine(_folder, "tiles", "0", "0_0.png")));
        }
    }
}
