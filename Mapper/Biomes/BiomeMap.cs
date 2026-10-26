using System.Collections.Concurrent;
using WorldEditor;

namespace Mapper
{
    // One stretch of a biome: the 4 by 4 block cells the game keeps biomes in, each touching another by a side. Its
    // middle is the cell nearest the middle of them all, so a curved one's middle is still in it
    public sealed class BiomePatch
    {
        public Dimension Dimension { get; }
        // As Java names it, "minecraft:cherry_grove"; Bedrock worlds' are translated to it
        public string Biome { get; }
        // "Cherry Grove"
        public string Name { get; }

        public int Cells => _cellX.Length;
        // Blocks across, both ends included
        public int MinX { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxZ { get; }
        public int Width => MaxX - MinX + 1;
        public int Depth => MaxZ - MinZ + 1;

        // Its middle block
        public int X { get; }
        public int Z { get; }

        private readonly int[] _cellX, _cellZ;
        private IReadOnlyList<(int Z, int X0, int X1)>? _runs;

        internal BiomePatch(Dimension dimension, string biome, int[] cellX, int[] cellZ)
        {
            Dimension = dimension;
            Biome = biome;
            Name = ColumnInspector.DisplayName(biome);
            _cellX = cellX;
            _cellZ = cellZ;

            double meanX = cellX.Average(), meanZ = cellZ.Average();
            int middle = 0;
            double best = double.MaxValue;
            for (int i = 0; i < cellX.Length; i++)
            {
                // As near as another: the more northern, then western, so it's the same every time
                double distance = (cellX[i] - meanX) * (cellX[i] - meanX) + (cellZ[i] - meanZ) * (cellZ[i] - meanZ);
                if (distance < best || distance == best && (cellZ[i] < cellZ[middle] || cellZ[i] == cellZ[middle] && cellX[i] < cellX[middle]))
                {
                    best = distance;
                    middle = i;
                }
            }
            X = cellX[middle] * 4 + 2;
            Z = cellZ[middle] * 4 + 2;
            MinX = cellX.Min() * 4;
            MinZ = cellZ.Min() * 4;
            MaxX = cellX.Max() * 4 + 3;
            MaxZ = cellZ.Max() * 4 + 3;
        }

        // Its cells, by 4 by 4 block cell
        public IEnumerable<(int X, int Z)> CellsIn()
        {
            for (int i = 0; i < _cellX.Length; i++) yield return (_cellX[i], _cellZ[i]);
        }

        // Its cells in rows: each row of cells z, from cell X0 to X1, in order
        public IReadOnlyList<(int Z, int X0, int X1)> Runs => _runs ??= MakeRuns();

        // The block of it nearest x, z, and how far that is (0 within it)
        public (int X, int Z, double Distance) NearestTo(double x, double z)
        {
            int bestX = X, bestZ = Z;
            double best = double.MaxValue;
            for (int i = 0; i < _cellX.Length; i++)
            {
                int left = _cellX[i] * 4, top = _cellZ[i] * 4;
                double nearX = Math.Clamp(x, left, left + 3.999), nearZ = Math.Clamp(z, top, top + 3.999);
                double distance = (nearX - x) * (nearX - x) + (nearZ - z) * (nearZ - z);
                if (distance >= best) continue;
                best = distance;
                bestX = (int)Math.Floor(nearX);
                bestZ = (int)Math.Floor(nearZ);
                if (distance == 0) break;
            }
            return (bestX, bestZ, Math.Sqrt(best));
        }

        private List<(int, int, int)> MakeRuns()
        {
            int[] order = [.. Enumerable.Range(0, _cellX.Length).OrderBy(i => _cellZ[i]).ThenBy(i => _cellX[i])];
            List<(int, int, int)> output = [];
            for (int i = 0; i < order.Length;)
            {
                int z = _cellZ[order[i]], start = _cellX[order[i]], end = start;
                i++;
                while (i < order.Length && _cellZ[order[i]] == z && _cellX[order[i]] == end + 1)
                {
                    end++;
                    i++;
                }
                output.Add((z, start, end));
            }
            return output;
        }

        public override string ToString() => $"{Name} at {X}, {Z}";
    }

    // Which biomes one region's cells have, a bit per cell for each biome found there. A cell can have several: the
    // surface's, and a cave biome under it
    internal sealed class RegionBiomes
    {
        public const int Side = 128;

        public Dictionary<string, ulong[]> Bits { get; } = [];

        public void Set(string biome, int x, int z)
        {
            lock (Bits)
            {
                if (!Bits.TryGetValue(biome, out ulong[]? bits)) Bits[biome] = bits = new ulong[Side * Side / 64];
                int index = (z & (Side - 1)) * Side + (x & (Side - 1));
                bits[index >> 6] |= 1UL << (index & 63);
            }
        }
    }

    // One dimension's biomes, as far as read: each in patches, kept by region so an update reads again only the
    // regions the game has saved since
    public sealed class DimensionBiomes
    {
        // A patch smaller than this, a few cells at the edge of a bigger one, is left out, unless it's the biggest of
        // its biome
        public const int MinCells = 4;

        public Dimension Dimension { get; }

        // Biggest first
        public IReadOnlyList<BiomePatch> Patches { get; }

        // The biomes found, by name
        public IReadOnlyList<string> Biomes { get; }

        internal IReadOnlyDictionary<Coords, RegionBiomes> ByRegion { get; }

        public IReadOnlyCollection<Coords> Read { get; }

        internal DimensionBiomes(Dimension dimension, Dictionary<Coords, RegionBiomes> byRegion, IReadOnlyCollection<Coords> read)
        {
            Dimension = dimension;
            ByRegion = byRegion;
            Read = read;
            Patches = Join(dimension, byRegion);
            Biomes = [.. Patches.Select(p => p.Biome).Distinct().OrderBy(ColumnInspector.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        }

        // The patches nearest x, z first, each with its block nearest there
        public static List<(BiomePatch Patch, int X, int Z, double Distance)> ByDistance(IEnumerable<BiomePatch> patches, double x, double z) =>
            [.. patches.Select(p =>
            {
                var (nearX, nearZ, distance) = p.NearestTo(x, z);
                return (p, nearX, nearZ, distance);
            }).OrderBy(p => p.distance).ThenByDescending(p => p.p.Cells)];

        // Cells of a biome touching by a side, across regions too
        private static List<BiomePatch> Join(Dimension dimension, Dictionary<Coords, RegionBiomes> byRegion)
        {
            Dictionary<string, Dictionary<Coords, ulong[]>> byBiome = [];
            foreach (var (region, biomes) in byRegion)
            {
                foreach (var (biome, bits) in biomes.Bits)
                {
                    if (!byBiome.TryGetValue(biome, out var regions)) byBiome[biome] = regions = [];
                    regions[region] = (ulong[])bits.Clone();
                }
            }

            const int side = RegionBiomes.Side;
            List<BiomePatch> output = [];
            List<int> xs = [], zs = [];
            Stack<(int X, int Z)> stack = new();
            foreach (var (biome, regions) in byBiome)
            {
                List<BiomePatch> found = [];

                // Takes the cell's bit, saying whether it was there
                bool Take(int x, int z)
                {
                    if (!regions.TryGetValue(new Coords(x >> 7, z >> 7), out ulong[]? bits)) return false;
                    int index = (z & (side - 1)) * side + (x & (side - 1));
                    ulong bit = 1UL << (index & 63);
                    if ((bits[index >> 6] & bit) == 0) return false;
                    bits[index >> 6] &= ~bit;
                    return true;
                }

                foreach (var (region, bits) in regions.OrderBy(r => r.Key.Z).ThenBy(r => r.Key.X))
                {
                    for (int word = 0; word < bits.Length; word++)
                    {
                        while (bits[word] != 0)
                        {
                            int index = word * 64 + System.Numerics.BitOperations.TrailingZeroCount(bits[word]);
                            int startX = region.X * side + index % side, startZ = region.Z * side + index / side;
                            Take(startX, startZ);
                            xs.Clear();
                            zs.Clear();
                            stack.Push((startX, startZ));
                            while (stack.Count > 0)
                            {
                                var (x, z) = stack.Pop();
                                xs.Add(x);
                                zs.Add(z);
                                if (Take(x + 1, z)) stack.Push((x + 1, z));
                                if (Take(x - 1, z)) stack.Push((x - 1, z));
                                if (Take(x, z + 1)) stack.Push((x, z + 1));
                                if (Take(x, z - 1)) stack.Push((x, z - 1));
                            }
                            found.Add(new BiomePatch(dimension, biome, [.. xs], [.. zs]));
                        }
                    }
                }

                BiomePatch biggest = found.MaxBy(p => p.Cells)!;
                output.AddRange(found.Where(p => p.Cells >= MinCells || p == biggest));
            }
            output.Sort((a, b) => a.Cells != b.Cells ? b.Cells.CompareTo(a.Cells) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
            return output;
        }
    }

    // Reading the biomes of one dimension. A column's biome is the one at its surface, where the map is drawn, and
    // cave biomes (lush caves, dripstone caves, the deep dark) count wherever they are under it. The Nether has no
    // surface to go by, so there every biome at any height counts
    public static class BiomeMap
    {
        public static readonly IReadOnlySet<string> CaveBiomes = new HashSet<string>(["minecraft:lush_caves", "minecraft:dripstone_caves", "minecraft:deep_dark", "minecraft:sulfur_caves"]);

        private const int RegionsInParallel = 4;
        private const int ChunksInParallel = 4;

        public static DimensionBiomes Read(SceneInfo scene, CancellationToken cancellation = default) => Update(null, scene, null, cancellation);

        public static DimensionBiomes None(Dimension dimension) => new(dimension, [], []);

        // earlier, read again in the regions given; everything when there's nothing earlier or no regions are given
        public static DimensionBiomes Update(DimensionBiomes? earlier, SceneInfo scene, IReadOnlyCollection<Coords>? regions, CancellationToken cancellation = default)
        {
            HashSet<Coords> exist = [.. scene.Regions];
            bool whole = earlier is null || regions is null;
            List<Coords> toRead = whole ? [.. exist] : [.. regions!.Where(exist.Contains)];

            Dictionary<Coords, RegionBiomes> byRegion = whole ? [] : earlier!.ByRegion.Where(r => exist.Contains(r.Key) && !regions!.Contains(r.Key)).ToDictionary();
            foreach (Coords region in toRead) byRegion.Remove(region);

            ConcurrentDictionary<Coords, RegionBiomes> found = [];
            ApiChunkLoadOptions options = new() { KeepDataTag = false };
            options.BiomeOptions.KeepDataTag = false;
            foreach (ObjectLoadOptions skipped in new[] { options.BlockStateOptions, options.BlockLightOptions, options.SkyLightOptions }) skipped.LoadObject = false;

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => scene.RegionStore,
                new ChunkEnumeratorFromRegionFactory(_ => new ApiChunkReader() { LoadOptions = options })) { Light = false };
            IVersionConverter converter = new ApiChunkVersionConverter();
            bool everyHeight = scene.Dimension == Dimension.Nether;
            // Bedrock has one biome in the End, which the translation table gives as Java's end_barrens
            bool bedrockEnd = scene.Dimension == Dimension.TheEnd && scene.RegionStore is BedrockRegionStore;
            enumerator.Enumerate([.. toRead], new Body(cancellation, chunk =>
            {
                ConvertedApiChunk? converted = chunk is ApiChunk api ? converter.Convert(api, api.Version, WorldEditor.Version.Newest, UsageIntent.Read) as ConvertedApiChunk : chunk as ConvertedApiChunk;
                if (converted is null) return;
                RegionBiomes region = found.GetOrAdd(new Coords(chunk.X >> 5, chunk.Z >> 5), _ => new RegionBiomes());
                Add(converted, everyHeight, region, bedrockEnd ? b => b == "minecraft:end_barrens" ? "minecraft:the_end" : b : null);
            }));
            cancellation.ThrowIfCancellationRequested();

            foreach (var (region, biomes) in found) byRegion[region] = biomes;
            return new DimensionBiomes(scene.Dimension, byRegion, toRead);
        }

        // A chunk's 16 cells: the biome at each one's surface, and any cave biome under it
        internal static void Add(ConvertedApiChunk chunk, bool everyHeight, RegionBiomes into, Func<string, string>? rename = null)
        {
            if (chunk.Biome is not BiomeChunk biomes || biomes.Sections.Count == 0) return;
            Dictionary<int, PaletteSection<string>> sections = [];
            foreach (PaletteSection<string> section in biomes.Sections)
            {
                if (section is not null && section.Palette.Length > 0) sections[section.Y] = section;
            }
            if (sections.Count == 0) return;

            short[]? heights = null;
            short floor = 0;
            if (!everyHeight && chunk.Heightmap?.Heightmaps.TryGetValue("WORLD_SURFACE", out HeightmapCollection.Heightmap? heightmap) == true)
            {
                heights = new short[256];
                heightmap.Unlock(heights);
                floor = heightmap.FloorY;
            }

            Dictionary<PaletteSection<string>, short[]> unlocked = new(ReferenceEqualityComparer.Instance);
            string Biome(PaletteSection<string> section, int cellX, int layer, int cellZ)
            {
                if (section.Type == SectionType.Uniform || section.Locker is null || section.Palette.Length == 1) return rename is null ? section.Palette[0] : rename(section.Palette[0]);
                if (!unlocked.TryGetValue(section, out short[]? indexes))
                {
                    indexes = new short[section.Locker.UnlockedArrayLength];
                    section.Unlock(indexes);
                    unlocked[section] = indexes;
                }
                int index = indexes[cellX + layer * 16 + cellZ * 4];
                string name = (uint)index < (uint)section.Palette.Length ? section.Palette[index] : section.Palette[0];
                return rename is null ? name : rename(name);
            }

            int lowest = sections.Keys.Min(), highest = sections.Keys.Max();
            List<PaletteSection<string>> withCaves = [.. sections.Values.Where(s => s.Palette.Any(CaveBiomes.Contains))];

            for (int cellZ = 0; cellZ < 4; cellZ++)
            {
                for (int cellX = 0; cellX < 4; cellX++)
                {
                    int x = chunk.X * 4 + cellX, z = chunk.Z * 4 + cellZ;
                    if (heights is null)
                    {
                        // Every height, as the Nether's biomes change with it
                        foreach (PaletteSection<string> section in sections.Values)
                        {
                            for (int layer = 0; layer < 4; layer++) into.Set(Biome(section, cellX, layer, cellZ), x, z);
                        }
                        continue;
                    }

                    // An empty column, as over the End's void, has no biome to show
                    short height = heights[(cellZ * 4 + 2) * 16 + cellX * 4 + 2];
                    if (height <= floor) continue;

                    int y = height - 1, sectionY = Math.Clamp(MathUtilities.FindSectionY(y), lowest, highest);
                    // A missing section, in an old chunk that keeps biomes in fewer, takes the nearest below or above
                    PaletteSection<string>? surface = null;
                    for (int step = 0; surface is null && step <= highest - lowest; step++)
                    {
                        surface = sections.GetValueOrDefault(sectionY - step) ?? sections.GetValueOrDefault(sectionY + step);
                    }
                    if (surface is null) continue;
                    int surfaceLayer = surface.Y == MathUtilities.FindSectionY(y) ? MathUtilities.NegMod(y, 16) / 4 : surface.Y < sectionY ? 3 : 0;
                    into.Set(Biome(surface, cellX, surfaceLayer, cellZ), x, z);

                    foreach (PaletteSection<string> section in withCaves)
                    {
                        for (int layer = 0; layer < 4; layer++)
                        {
                            string biome = Biome(section, cellX, layer, cellZ);
                            if (CaveBiomes.Contains(biome)) into.Set(biome, x, z);
                        }
                    }
                }
            }
        }

        private sealed class Body(CancellationToken cancellation, Action<IChunk> read) : IEnumerationBody
        {
            public void BeginCycle() => cancellation.ThrowIfCancellationRequested();
            public void EndCycle() { }
            public void BeginReadingRegion(int index, Coords region) { }
            public void EndReadingRegion(int index, Coords region) { }

            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk)
            {
                if (!cancellation.IsCancellationRequested) read(chunk);
            }
        }
    }

    public static class BiomeText
    {
        // Whether a biome's name has every word, in any case; "cherry_grove" works as well as "cherry grove"
        public static bool Says(string biome, IReadOnlyCollection<string> words)
        {
            if (words.Count == 0) return true;
            string name = ColumnInspector.DisplayName(biome);
            return words.All(w => name.Contains(w, StringComparison.CurrentCultureIgnoreCase) || biome.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
    }

    // A rectangle of a biome patch on the screen: Picked for the patch chosen in the list, drawn more solidly
    public readonly record struct BiomeMark(double X, double Y, double Width, double Height, bool Picked);

    // Where biome patches go on the screen, row by row. Zoomed out, cells come together in squares of a few pixels,
    // so a sea of them stays a few thousand rectangles. Each GUI's painter only draws it
    public static class BiomeFrame
    {
        public const string Color = "#d500f9";
        public const double ShadedOpacity = 0.3, PickedOpacity = 0.6;
        // Pixels a cell's square is at least
        private const double MinCell = 3;

        public static List<BiomeMark> Build(IReadOnlyList<BiomePatch> shaded, BiomePatch? picked, IMarkupView view, double width, double height)
        {
            List<BiomeMark> output = [];
            var (originX, _) = view.ToScreen(0, 0);
            double zoom = view.ToScreen(1, 0).X - originX;
            if (zoom <= 0) return output;

            var (left, top) = view.ToWorld(0, 0);
            var (right, bottom) = view.ToWorld(width, height);
            // Cells per square: 1 zoomed in, more by powers of two zoomed out
            int step = 1;
            while (step * 4 * zoom < MinCell && step < 1 << 12) step *= 2;
            int size = step * 4;

            void Add(IEnumerable<BiomePatch> patches, bool isPicked)
            {
                HashSet<(int X, int Z)> squares = [];
                foreach (BiomePatch patch in patches)
                {
                    if (patch.MaxX < left || patch.MinX > right || patch.MaxZ < top || patch.MinZ > bottom) continue;
                    if (step == 1)
                    {
                        foreach (var (z, x0, x1) in patch.Runs)
                        {
                            if (z * 4 + 4 < top || z * 4 > bottom || x1 * 4 + 4 < left || x0 * 4 > right) continue;
                            output.Add(Mark(x0 * 4, z * 4, (x1 - x0 + 1) * 4, 4, isPicked));
                        }
                        continue;
                    }
                    foreach (var (x, z) in patch.CellsIn())
                    {
                        if (x * 4 + 4 < left || x * 4 > right || z * 4 + 4 < top || z * 4 > bottom) continue;
                        squares.Add((x >> BitShift(step), z >> BitShift(step)));
                    }
                }
                foreach (var row in squares.GroupBy(s => s.Z))
                {
                    int[] xs = [.. row.Select(s => s.X).Order()];
                    for (int i = 0; i < xs.Length;)
                    {
                        int start = xs[i], end = start;
                        while (++i < xs.Length && xs[i] == end + 1) end++;
                        output.Add(Mark(start * size, row.Key * size, (end - start + 1) * size, size, isPicked));
                    }
                }
            }

            BiomeMark Mark(int blockX, int blockZ, int blocksWide, int blocksHigh, bool isPicked)
            {
                var (x, y) = view.ToScreen(blockX, blockZ);
                return new BiomeMark(x, y, blocksWide * zoom, blocksHigh * zoom, isPicked);
            }

            Add(picked is null ? shaded : shaded.Where(p => p != picked), false);
            if (picked is not null) Add([picked], true);
            return output;
        }

        private static int BitShift(int step) => System.Numerics.BitOperations.Log2((uint)step);
    }
}
