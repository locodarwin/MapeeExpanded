using StbImageSharp;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using WorldEditor;

namespace Mapper
{
    public record WebMapMarker(string Name, double X, double Z);

    // What the viewer shows around the map
    public class WebMapInfo
    {
        public string Title { get; init; } = "Map";
        public string Subtitle { get; init; } = string.Empty;
        public IList<WebMapMarker> Markers { get; init; } = [];
    }

    public enum WebMapStage { Rendering, Zooming, Done }
    public readonly record struct WebMapProgress(WebMapStage Stage, int Done, int Total);

    // Lays a rendered map out as a folder a browser can open: index.html plus tiles/<level>/<x>_<z>.png.
    // Level 0 is one tile per region at one pixel per block; each level below halves that, down to where the
    // whole map fits in a few tiles. The viewer is a single page with no outside scripts, so the folder
    // works straight from disk or on any web server
    public class WebMapWriter
    {
        public const int TileSize = 512;
        public const string Marker = "mapee-web-map";

        // Deep enough for a world 128 regions (65,536 blocks) across to fit on one screen
        private const int LowestLevel = -8;

        public string OutputDirectory { get; }

        private readonly string _tiles;
        private readonly ConcurrentDictionary<Coords, byte> _regions = new();

        // Checks the folder is empty or holds an earlier export, whose tiles are then cleared
        public WebMapWriter(string outputDirectory)
        {
            OutputDirectory = outputDirectory;
            _tiles = Path.Combine(outputDirectory, "tiles");

            Directory.CreateDirectory(outputDirectory);
            if (IsEarlierExport(outputDirectory))
            {
                if (Directory.Exists(_tiles)) Directory.Delete(_tiles, true);
            }
            else if (Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new IOException("The folder isn't empty. Choose an empty folder, or one holding an earlier web map export to replace it.");
            }

            Directory.CreateDirectory(Path.Combine(_tiles, "0"));
        }

        public static bool IsEarlierExport(string directory)
        {
            string index = Path.Combine(directory, "index.html");
            return File.Exists(index) && File.ReadAllText(index).Contains(Marker);
        }

        // Takes a region as the map renderer draws it: Bgra32 rows covering blocks from (left, top), possibly
        // less than the whole region. Safe to call from several threads
        public void AddRegion(Coords region, int left, int top, int width, int height, byte[] bgra)
        {
            byte[] tile = new byte[TileSize * TileSize * 4];
            int offsetX = MathUtilities.NegMod(left, TileSize), offsetZ = MathUtilities.NegMod(top, TileSize);
            bool drawn = false;

            for (int y = 0; y < height && offsetZ + y < TileSize; y++)
            {
                for (int x = 0; x < width && offsetX + x < TileSize; x++)
                {
                    int from = (y * width + x) * 4, to = ((offsetZ + y) * TileSize + offsetX + x) * 4;
                    if (bgra[from + 3] == 0) continue;

                    tile[to] = bgra[from + 2];
                    tile[to + 1] = bgra[from + 1];
                    tile[to + 2] = bgra[from];
                    tile[to + 3] = bgra[from + 3];
                    drawn = true;
                }
            }
            if (!drawn) return;

            PngWriter.Write(TilePath(0, region.X, region.Z), tile, TileSize, TileSize);
            _regions[region] = 0;
        }

        // Makes the zoomed-out levels and the page. Call once every region is added
        public void Finish(WebMapInfo info, IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default)
        {
            List<HashSet<Coords>> levels = [[.. _regions.Keys]];
            int total = CountLevels(levels[0]).Sum(), done = 0;

            while (levels[^1].Count > 4 && -levels.Count >= LowestLevel)
            {
                int level = -levels.Count;
                HashSet<Coords> children = levels[^1];
                HashSet<Coords> parents = [.. children.Select(c => new Coords(MathUtilities.FindSectionY(c.X, 2), MathUtilities.FindSectionY(c.Z, 2)))];
                Directory.CreateDirectory(Path.Combine(_tiles, level.ToString()));

                Parallel.ForEach(parents, new ParallelOptions() { CancellationToken = cancellation }, parent =>
                {
                    WriteParent(level, parent, children);
                    progress?.Report(new WebMapProgress(WebMapStage.Zooming, Interlocked.Increment(ref done), total));
                });
                levels.Add(parents);
            }

            File.WriteAllText(Path.Combine(OutputDirectory, "index.html"), CreatePage(info, levels[0], -(levels.Count - 1)));
            progress?.Report(new WebMapProgress(WebMapStage.Done, total, total));
        }

        // How many tiles each level below 0 will have, for progress
        private static IEnumerable<int> CountLevels(HashSet<Coords> regions)
        {
            HashSet<Coords> level = regions;
            for (int depth = 1; level.Count > 4 && -depth >= LowestLevel; depth++)
            {
                level = [.. level.Select(c => new Coords(MathUtilities.FindSectionY(c.X, 2), MathUtilities.FindSectionY(c.Z, 2)))];
                yield return level.Count;
            }
        }

        // Each of the four tiles below is halved into its quarter, averaging colours by how opaque they are
        private void WriteParent(int level, Coords parent, HashSet<Coords> children)
        {
            byte[] tile = new byte[TileSize * TileSize * 4];
            const int half = TileSize / 2;

            for (int quarter = 0; quarter < 4; quarter++)
            {
                Coords child = new(parent.X * 2 + quarter % 2, parent.Z * 2 + quarter / 2);
                if (!children.Contains(child)) continue;

                ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(TilePath(level + 1, child.X, child.Z)), ColorComponents.RedGreenBlueAlpha);
                byte[] source = image.Data;
                int baseX = quarter % 2 * half, baseY = quarter / 2 * half;

                for (int y = 0; y < half; y++)
                {
                    for (int x = 0; x < half; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            int from = ((y * 2 + i / 2) * TileSize + x * 2 + i % 2) * 4;
                            int alpha = source[from + 3];
                            r += source[from] * alpha;
                            g += source[from + 1] * alpha;
                            b += source[from + 2] * alpha;
                            a += alpha;
                        }
                        if (a == 0) continue;

                        int to = ((baseY + y) * TileSize + baseX + x) * 4;
                        tile[to] = (byte)(r / a);
                        tile[to + 1] = (byte)(g / a);
                        tile[to + 2] = (byte)(b / a);
                        tile[to + 3] = (byte)((a + 2) / 4);
                    }
                }
            }

            PngWriter.Write(TilePath(level, parent.X, parent.Z), tile, TileSize, TileSize);
        }

        private string TilePath(int level, int x, int z)
        {
            return Path.Combine(_tiles, level.ToString(), $"{x}_{z}.png");
        }

        private static string CreatePage(WebMapInfo info, HashSet<Coords> regions, int minLevel)
        {
            var data = new
            {
                title = info.Title,
                subtitle = info.Subtitle,
                tileSize = TileSize,
                minLevel,
                regions = regions.OrderBy(c => c.Z).ThenBy(c => c.X).SelectMany(c => new[] { c.X, c.Z }),
                markers = info.Markers.Select(m => new { name = m.Name, x = m.X, z = m.Z })
            };
            string json = JsonSerializer.Serialize(data).Replace("</", "<\\/");

            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mapper.Export.WebMapViewer.html")
                ?? throw new InvalidOperationException("The web map viewer is missing from the build");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd()
                .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(info.Title))
                .Replace("/*{{DATA}}*/null", json);
        }
    }
}
