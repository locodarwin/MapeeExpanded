using Mapper;
using StbImageSharp;
using System.IO.Compression;
using System.Text.RegularExpressions;
using WorldEditor;

namespace Mapee.Tests.Export
{
    // The web map export's tiles, heights, zoom levels, page and folder handling, from regions drawn by hand
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
            WebMapWriter writer = new(_folder, "overworld");
            Coords[] regions = [new(0, 0), new(1, 0), new(-1, -1), new(5, 5), new(6, 5), new(-8, 3)];
            foreach (Coords region in regions)
            {
                writer.AddRegion(region, region.X * 512, region.Z * 512, 512, 512, Solid(512, 512, 0, 0, 255));
            }
            WebMapDimension overworld = writer.Finish("Overworld", "Default", [new WebMapMarker("Spawn", 10, -20)]);
            WebMapPage.Write(_folder, "Tom & Jerry's", [overworld]);

            string tiles = Path.Combine(_folder, "overworld", "tiles");
            foreach (Coords region in regions) Assert.True(File.Exists(Path.Combine(tiles, "0", $"{region.X}_{region.Z}.png")));

            // Six regions halve to five tiles, then four, where it stops
            Assert.Equal(5, Directory.GetFiles(Path.Combine(tiles, "-1")).Length);
            Assert.Equal(4, Directory.GetFiles(Path.Combine(tiles, "-2")).Length);
            Assert.False(Directory.Exists(Path.Combine(tiles, "-3")));
            Assert.Equal(-2, overworld.MinLevel);

            // Region (0, 0) fills the top left quarter of tile (0, 0) below it; nothing is under the bottom half
            ImageResult parent = ReadTile(Path.Combine(tiles, "-1", "0_0.png"));
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
            WebMapWriter writer = new(_folder, "overworld");

            // Blocks x -300 to -201, z -512 to -503: region (-1, -1), from pixel (212, 0)
            writer.AddRegion(new Coords(-1, -1), -300, -512, 100, 10, Solid(100, 10, 255, 0, 0));
            writer.Finish("Overworld", "", []);

            ImageResult tile = ReadTile(Path.Combine(_folder, "overworld", "tiles", "0", "-1_-1.png"));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(tile, 212, 0));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(tile, 311, 9));
            Assert.Equal(0, Pixel(tile, 211, 0)[3]);
            Assert.Equal(0, Pixel(tile, 312, 0)[3]);
            Assert.Equal(0, Pixel(tile, 212, 10)[3]);
        }

        [Fact]
        public void EmptyRegionsWriteNoTile()
        {
            WebMapWriter writer = new(_folder, "overworld");
            writer.AddRegion(new Coords(0, 0), 0, 0, 512, 512, new byte[512 * 512 * 4]);
            writer.AddHeights(new Coords(0, 0), Heights(short.MinValue));
            WebMapPage.Write(_folder, "Map", [writer.Finish("Overworld", "", [])]);

            Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "overworld", "tiles", "0")));
            Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "overworld", "heights")));
            Assert.Contains("\"regions\":[]", File.ReadAllText(Path.Combine(_folder, "index.html")));
        }

        private static short[] Heights(short value)
        {
            short[] heights = new short[512 * 512];
            Array.Fill(heights, value);
            return heights;
        }

        [Fact]
        public void HeightsReadBackAsThePageDecodesThem()
        {
            short[] heights = Heights(64);
            heights[0] = short.MinValue;
            heights[1] = -64;
            heights[512 * 300 + 7] = 319;
            heights[^1] = short.MinValue;

            WebMapWriter writer = new(_folder, "nether");
            writer.AddHeights(new Coords(-2, 3), heights);

            string script = File.ReadAllText(Path.Combine(_folder, "nether", "heights", "-2_3.js"));
            Match call = Regex.Match(script, "^mapeeHeights\\(\"nether\",-2,3,\"([A-Za-z0-9+/=]+)\"\\);\n$");
            Assert.True(call.Success, script[..Math.Min(80, script.Length)]);

            // As the page does: inflate, then add up the differences along the rows, wrapping as 16 bits
            using MemoryStream inflated = new();
            using (DeflateStream deflate = new(new MemoryStream(Convert.FromBase64String(call.Groups[1].Value)), CompressionMode.Decompress)) deflate.CopyTo(inflated);
            byte[] bytes = inflated.ToArray();
            Assert.Equal(512 * 512 * 2, bytes.Length);

            short[] decoded = new short[512 * 512];
            for (int i = 0; i < decoded.Length; i++)
            {
                short delta = (short)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
                decoded[i] = unchecked((short)((i == 0 ? 0 : decoded[i - 1]) + delta));
            }
            Assert.Equal(heights, decoded);

            // Flat ground packs small
            Assert.True(script.Length < 4000, $"{script.Length} characters");
        }

        [Fact]
        public void ThePageListsEachDimensionWithItsFolder()
        {
            WebMapWriter overworld = new(_folder, "overworld");
            overworld.AddRegion(new Coords(0, 0), 0, 0, 512, 512, Solid(512, 512, 0, 255, 0));
            WebMapWriter nether = new(_folder, "nether");
            nether.AddRegion(new Coords(-1, 0), -512, 0, 512, 512, Solid(512, 512, 0, 0, 255));

            WebMapPage.Write(_folder, "World", [overworld.Finish("Overworld", "Default", []), nether.Finish("Nether", "Default · slice at Y 30", [])], "nether");

            string page = File.ReadAllText(Path.Combine(_folder, "index.html"));
            Assert.Contains("\"initial\":\"nether\"", page);
            Assert.Contains("\"id\":\"overworld\",\"name\":\"Overworld\"", page);
            Assert.Contains("\"id\":\"nether\",\"name\":\"Nether\",\"subtitle\":\"Default \\u00B7 slice at Y 30\"", page);
            Assert.True(File.Exists(Path.Combine(_folder, "overworld", "tiles", "0", "0_0.png")));
            Assert.True(File.Exists(Path.Combine(_folder, "nether", "tiles", "0", "-1_0.png")));
        }

        [Fact]
        public void AnInitialDimensionNotExportedFallsBackToTheFirst()
        {
            WebMapWriter overworld = new(_folder, "overworld");
            WebMapPage.Write(_folder, "World", [overworld.Finish("Overworld", "", [])], "the_end");

            Assert.Contains("\"initial\":\"overworld\"", File.ReadAllText(Path.Combine(_folder, "index.html")));
        }

        [Fact]
        public void AFolderWithOtherFilesIsUsedAndTheyStay()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "mine");

            Assert.Empty(WebMapFolder.WillReplace(_folder, ["overworld", "nether"]));
            WebMapFolder.Clear(_folder, ["overworld"]);
            WebMapWriter writer = new(_folder, "overworld");
            WebMapPage.Write(_folder, "Map", [writer.Finish("Overworld", "", [])]);

            Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
            Assert.True(File.Exists(Path.Combine(_folder, "index.html")));
        }

        [Fact]
        public void ReplacingAnEarlierExportIsNamedAndClearsItsTiles()
        {
            WebMapFolder.Clear(_folder, ["overworld"]);
            WebMapWriter first = new(_folder, "overworld");
            first.AddRegion(new Coords(3, 3), 1536, 1536, 512, 512, Solid(512, 512, 0, 255, 0));
            first.AddHeights(new Coords(3, 3), Heights(70));
            File.WriteAllText(Path.Combine(_folder, "overworld", "kept.txt"), "mine");
            WebMapPage.Write(_folder, "Map", [first.Finish("Overworld", "", [])]);

            Assert.Equal(["index.html", "overworld"], WebMapFolder.WillReplace(_folder, ["overworld", "nether"]));

            WebMapFolder.Clear(_folder, ["overworld", "nether"]);
            WebMapWriter second = new(_folder, "overworld");
            second.AddRegion(new Coords(0, 0), 0, 0, 512, 512, Solid(512, 512, 0, 255, 0));
            WebMapPage.Write(_folder, "Map", [second.Finish("Overworld", "", [])]);

            Assert.False(File.Exists(Path.Combine(_folder, "overworld", "tiles", "0", "3_3.png")));
            Assert.False(File.Exists(Path.Combine(_folder, "overworld", "heights", "3_3.js")));
            Assert.True(File.Exists(Path.Combine(_folder, "overworld", "tiles", "0", "0_0.png")));
            Assert.True(File.Exists(Path.Combine(_folder, "overworld", "kept.txt")));
        }

        [Fact]
        public void AnExportFromBeforeDimensionsLosesItsTopLevelTiles()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "tiles", "0"));
            File.WriteAllText(Path.Combine(_folder, "index.html"), $"<!-- {WebMapWriter.Marker} -->");

            Assert.Equal(["index.html", "tiles"], WebMapFolder.WillReplace(_folder, ["overworld"]));
            WebMapFolder.Clear(_folder, ["overworld"]);
            Assert.False(Directory.Exists(Path.Combine(_folder, "tiles")));
        }

        [Fact]
        public void DimensionsBeyondTheThreeGetSafeFolderNames()
        {
            Assert.Equal("the_end", WebMapFolder.IdOf(Dimension.TheEnd));
            Assert.Equal("mymod_deep_dark", WebMapFolder.IdOf(new Dimension("mymod:deep dark", "Deep Dark")));
        }
    }
}
