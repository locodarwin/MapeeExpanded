using StbImageSharp;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using WorldEditor;

namespace Mapper
{
    public record WebMapMarker(string Name, double X, double Z);

    // One dimension's map as the page lists it: its folder, what the switcher and the panel call it, and what
    // its writer drew
    public record WebMapDimension(string Id, string Name, string Subtitle, IList<WebMapMarker> Markers,
        IReadOnlyCollection<Coords> Regions, int MinLevel)
    {
        // How many of the regions with a tile this export drew, of how many there are; fewer when an earlier export's
        // unchanged regions were kept
        public int Redrawn { get; init; }
        public int Total { get; init; }

        // Whether an earlier export was updated, rather than everything drawn afresh
        public bool Updated { get; init; }
    }

    public enum WebMapStage { Rendering, Zooming, Done }
    // Update: only the regions that changed since an earlier export are being drawn, Total of them
    public readonly record struct WebMapProgress(WebMapStage Stage, int Done, int Total, string? Dimension = null, bool Update = false);

    // Lays one dimension's rendered map out under <folder>/<id>: tiles/<level>/<x>_<z>.png and
    // heights/<x>_<z>.js. Level 0 is one tile per region at one pixel per block; each level below halves that,
    // down to where the whole map fits in a few tiles. The heights give the page each block's Y; they're scripts
    // because a page opened from disk may run scripts beside it but not read files. WebMapPage writes the page
    // over all the dimensions, with no outside scripts, so the folder works straight from disk or on any web server
    public class WebMapWriter
    {
        public const int TileSize = 512;
        public const string Marker = "mapee-web-map";

        // Deep enough for a world 128 regions (65,536 blocks) across to fit on one screen
        private const int LowestLevel = -8;

        public string OutputDirectory { get; }
        public string Id { get; }

        private readonly string _tiles;
        private readonly string _heights;
        private readonly ConcurrentDictionary<Coords, byte> _regions = new();

        // Call WebMapFolder.Clear on the folder first, so no tiles are left from an earlier export, unless updating one:
        // then Keep its unchanged regions and Forget the rest before drawing them again
        public WebMapWriter(string outputDirectory, string id)
        {
            OutputDirectory = outputDirectory;
            Id = id;
            _tiles = Path.Combine(outputDirectory, id, "tiles");
            _heights = Path.Combine(outputDirectory, id, "heights");

            Directory.CreateDirectory(Path.Combine(_tiles, "0"));
            Directory.CreateDirectory(_heights);
        }

        // Regions an earlier export into this folder drew, whose tiles and heights stay as they are
        public void Keep(IEnumerable<Coords> regions)
        {
            foreach (Coords region in regions) _regions[region] = 0;
        }

        // Removes what an earlier export drew of a region, before it's drawn again or when it's gone from the world
        public void Forget(Coords region)
        {
            _regions.TryRemove(region, out _);
            File.Delete(TilePath(0, region.X, region.Z));
            File.Delete(HeightsPath(region));
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

        // The Y of the block drawn in each column of a region, rows from the north, short.MinValue where nothing
        // is. Stored as differences along the rows, deflated and in base64, as a call the page answers. Safe to
        // call from several threads
        public void AddHeights(Coords region, short[] heights)
        {
            if (heights.Length != TileSize * TileSize) throw new ArgumentException("A region's heights are 512 by 512", nameof(heights));
            if (Array.TrueForAll(heights, h => h == short.MinValue)) return;

            byte[] deltas = new byte[heights.Length * 2];
            short previous = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                short delta = unchecked((short)(heights[i] - previous));
                deltas[i * 2] = (byte)delta;
                deltas[i * 2 + 1] = (byte)(delta >> 8);
                previous = heights[i];
            }

            using MemoryStream compressed = new();
            using (DeflateStream deflate = new(compressed, CompressionLevel.Optimal, true)) deflate.Write(deltas);

            string script = $"mapeeHeights(\"{Id}\",{region.X},{region.Z},\"{Convert.ToBase64String(compressed.ToArray())}\");\n";
            string path = HeightsPath(region);
            File.Delete(path);
            File.WriteAllText(path, script);
        }

        // Makes the zoomed-out levels. Call once every region is added; the page takes what it returns. When updating
        // an earlier export, changed names the regions drawn again or forgotten, and only the tiles over them are
        // made again; tiles over regions no longer there are removed
        public WebMapDimension Finish(string name, string subtitle, IList<WebMapMarker> markers,
            IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default, IReadOnlySet<Coords>? changed = null)
        {
            HashSet<Coords> regions = [.. _regions.Keys];
            List<(int Level, HashSet<Coords> Children, HashSet<Coords> Parents, List<Coords> Write)> plan = [];

            // Null once a level is made whole, as every level below it then is too
            HashSet<Coords>? dirty = changed is null ? null : [.. changed];
            HashSet<Coords> children = regions;
            while (children.Count > 4 && -(plan.Count + 1) >= LowestLevel)
            {
                int level = -(plan.Count + 1);
                HashSet<Coords> parents = [.. children.Select(Parent)];

                if (dirty is not null && !Directory.Exists(Path.Combine(_tiles, level.ToString()))) dirty = null;
                if (dirty is not null) dirty = [.. dirty.Select(Parent)];

                plan.Add((level, children, parents, [.. dirty is null ? parents : parents.Where(dirty.Contains)]));
                children = parents;
            }

            int total = plan.Sum(step => step.Write.Count), done = 0;
            foreach (var (level, below, parents, write) in plan)
            {
                string folder = Path.Combine(_tiles, level.ToString());
                Directory.CreateDirectory(folder);
                RemoveTilesExcept(folder, parents);

                Parallel.ForEach(write, new ParallelOptions() { CancellationToken = cancellation }, parent =>
                {
                    WriteParent(level, parent, below);
                    progress?.Report(new WebMapProgress(WebMapStage.Zooming, Interlocked.Increment(ref done), total, name));
                });
            }

            // Levels an earlier export went deeper to
            for (int level = -(plan.Count + 1); level >= LowestLevel; level--)
            {
                string folder = Path.Combine(_tiles, level.ToString());
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }

            return new WebMapDimension(Id, name, subtitle, markers, regions, -plan.Count);
        }

        private static Coords Parent(Coords child) => new(MathUtilities.FindSectionY(child.X, 2), MathUtilities.FindSectionY(child.Z, 2));

        private static void RemoveTilesExcept(string folder, HashSet<Coords> keep)
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
            {
                string[] parts = Path.GetFileNameWithoutExtension(file).Split('_');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int z) && keep.Contains(new Coords(x, z))) continue;
                File.Delete(file);
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

        private string HeightsPath(Coords region)
        {
            return Path.Combine(_heights, $"{region.X}_{region.Z}.js");
        }
    }

    // The export folder: what an export there replaces, and clearing it beforehand
    public static class WebMapFolder
    {
        // The dimensions given folders of their own, by the ids their folders are named after
        public static readonly IReadOnlyList<(Dimension Dimension, string Id)> Dimensions =
            [(Dimension.Overworld, "overworld"), (Dimension.Nether, "nether"), (Dimension.TheEnd, "the_end")];

        public static string IdOf(Dimension dimension)
        {
            foreach ((Dimension known, string id) in Dimensions) if (known == dimension) return id;

            string name = dimension.Namespace.Replace(':', '_');
            return new string([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_')]);
        }

        public static bool IsEarlierExport(string directory)
        {
            string index = Path.Combine(directory, "index.html");
            return File.Exists(index) && File.ReadAllText(index).Contains(WebMapWriter.Marker);
        }

        // What in the folder an export of these dimensions writes over, by name; empty when nothing is
        public static IList<string> WillReplace(string directory, IEnumerable<string> ids)
        {
            if (!Directory.Exists(directory)) return [];

            List<string> names = ["index.html", .. ids, .. IsEarlierExport(directory) ? ["tiles"] : Array.Empty<string>()];
            return [.. names.Where(name => Path.Exists(Path.Combine(directory, name)))];
        }

        public static bool IsEmpty(string directory)
        {
            return !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any();
        }

        // Removes the tiles and heights of these dimensions, and the single map an export before dimensions left
        // at the top. Anything else in the folder stays
        public static void Clear(string directory, IEnumerable<string> ids)
        {
            Directory.CreateDirectory(directory);
            if (IsEarlierExport(directory)) Delete(Path.Combine(directory, "tiles"));

            foreach (string id in ids) ClearDimension(directory, id);
        }

        // One dimension's tiles and heights, and the record of what drew them
        public static void ClearDimension(string directory, string id)
        {
            Delete(Path.Combine(directory, id, "tiles"));
            Delete(Path.Combine(directory, id, "heights"));
            WebMapManifest.Delete(directory, id);
        }

        private static void Delete(string directory)
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    // The page over the dimensions: index.html, the viewer with the dimensions' data in it,
    // and, when given, the world's pins, zones and shown signs and mobs
    public static class WebMapPage
    {
        public static void Write(string directory, string title, IList<WebMapDimension> dimensions, string? initial = null, MarkupDocument? markup = null)
        {
            bool In(MarkupItem item, WebMapDimension dimension) => WebMapFolder.IdOf(new Dimension(item.Dimension)) == dimension.Id;

            var data = new
            {
                title,
                tileSize = WebMapWriter.TileSize,
                initial = dimensions.Any(d => d.Id == initial) ? initial : dimensions.FirstOrDefault()?.Id,
                icons = PinIcons.All.ToDictionary(i => i.Id, i => i.Path),
                paw = PetShape.Paw,
                dimensions = dimensions.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    subtitle = d.Subtitle,
                    minLevel = d.MinLevel,
                    regions = d.Regions.OrderBy(c => c.Z).ThenBy(c => c.X).SelectMany(c => new[] { c.X, c.Z }),
                    markers = d.Markers.Select(m => new { name = m.Name, x = m.X, z = m.Z }),
                    pins = (markup?.Pins ?? []).Where(p => In(p, d)).Select(p => new { label = p.Label, x = p.X, z = p.Z, color = p.Color, icon = p.Icon, note = p.Note }),
                    signs = (markup?.Signs ?? []).Where(s => WebMapFolder.IdOf(new Dimension(s.Dimension)) == d.Id).Select(s => new { x = s.X, y = s.Y, z = s.Z, lines = s.Lines }),
                    pets = (markup?.Pets ?? []).Where(p => WebMapFolder.IdOf(new Dimension(p.Dimension)) == d.Id).Select(p => new { x = p.X, y = p.Y, z = p.Z, name = p.Name, kind = p.Description }),
                    // Largest first, so smaller zones lie on top
                    zones = (markup?.Zones ?? []).Where(z => In(z, d)).OrderByDescending(z => ZoneGeometry.Area(z.Points)).Select(z =>
                    {
                        var (lx, lz) = ZoneGeometry.LabelPoint(z.Points);
                        return new
                        {
                            label = z.Label,
                            color = z.Color,
                            note = z.Note,
                            size = ZoneGeometry.Describe(z.Points),
                            labelAt = new[] { Math.Round(lx, 2), Math.Round(lz, 2) },
                            points = z.Points.SelectMany(p => new[] { p.X, p.Z })
                        };
                    })
                })
            };
            string json = JsonSerializer.Serialize(data).Replace("</", "<\\/");

            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mapper.Export.WebMapViewer.html")
                ?? throw new InvalidOperationException("The web map viewer is missing from the build");
            using StreamReader reader = new(stream);
            string page = reader.ReadToEnd()
                .Replace("{{TITLE}}", System.Net.WebUtility.HtmlEncode(title))
                .Replace("/*{{DATA}}*/null", json);

            Directory.CreateDirectory(directory);
            string index = Path.Combine(directory, "index.html");
            File.Delete(index);
            File.WriteAllText(index, page);
        }
    }
}
