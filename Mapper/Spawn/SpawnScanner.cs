using WorldEditor;

namespace Mapper
{
    // Which columns of one region have somewhere hostile mobs can spawn, a bit for each of its 512 by 512 columns
    public class SpawnRegion
    {
        public const int Size = 512;

        public Coords Region { get; }
        public int Count { get; private set; }

        private readonly ulong[] _bits = new ulong[Size * Size / 64];

        public SpawnRegion(Coords region)
        {
            Region = region;
        }

        // x and z within the region
        public bool this[int x, int z] => (_bits[(z * Size + x) >> 6] & (1UL << ((z * Size + x) & 63))) != 0;

        internal void Set(int x, int z)
        {
            int index = z * Size + x;
            ulong bit = 1UL << (index & 63);
            lock (_bits)
            {
                if ((_bits[index >> 6] & bit) != 0) return;
                _bits[index >> 6] |= bit;
                Count++;
            }
        }
    }

    // Reads chunks with their blocks, biomes and block light, and for each column finds the floor the map shows
    // and whether mobs can spawn on it. Looking down from the top, or from a height cut: the first solid block below
    // open space is the floor, so a cut through a building or into the rock shows the floors under it. A floor
    // needs two open blocks above it, which the top of the world or the cut counts as having
    public class SpawnScanner
    {
        private const int RegionsInParallel = 2;
        private const int ChunksInParallel = 4;

        // Where to look down from, as the map draws it; null for the top of the world
        public int? StartY { get; }

        [ThreadStatic] private static Dictionary<object, short[]>? _unlocked;

        public SpawnScanner(int? startY = null)
        {
            StartY = startY;
        }

        public void Scan(IRegionStore store, IReadOnlyList<Coords> regions, Action<SpawnRegion> found, CancellationToken cancellation = default)
        {
            ApiChunkLoadOptions options = new() { KeepDataTag = false };
            foreach (ObjectLoadOptions kept in new[] { options.BlockStateOptions, options.BiomeOptions, options.BlockLightOptions }) kept.KeepDataTag = false;
            foreach (ObjectLoadOptions skipped in new[] { options.SkyLightOptions, options.HeightmapOptions }) skipped.LoadObject = false;

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => store,
                new ChunkEnumeratorFromRegionFactory(_ => new ApiChunkReader() { LoadOptions = options })) { SkyLight = false };

            enumerator.Enumerate([.. regions], new Body(this, found, cancellation));
            cancellation.ThrowIfCancellationRequested();
        }

        private readonly IVersionConverter _converter = new ApiChunkVersionConverter();

        // Marks the chunk's columns where mobs can spawn in its region's map
        public void Scan(IChunk chunk, SpawnRegion into)
        {
            ConvertedApiChunk? converted = chunk is ApiChunk api ? _converter.Convert(api, api.Version, WorldEditor.Version.Newest, UsageIntent.Read) as ConvertedApiChunk : chunk as ConvertedApiChunk;
            if (converted?.BlockState is not BlockStateChunk blocks || blocks.Sections.Count == 0) return;

            ChunkColumns columns = new(converted);
            int left = MathUtilities.NegMod(chunk.X, 32) * 16, top = MathUtilities.NegMod(chunk.Z, 32) * 16;
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    if (CanSpawn(columns, x, z, StartY)) into.Set(left + x, top + z);
                }
            }
        }

        // Down to the first floor below open space, then whether a mob fits there in the dark
        private static bool CanSpawn(ChunkColumns column, int x, int z, int? startY)
        {
            int y = Math.Min(startY ?? int.MaxValue, column.Top);
            int open = 0;

            // Starting in open space, what's above counts as open; starting inside a block, the scan goes down to
            // the space below it first
            bool started = column.Kind(x, y, z) is SpawnKind.Open;
            if (started) open = 2;

            for (; y >= column.Bottom; y--)
            {
                SpawnKind kind = column.Kind(x, y, z);
                if (kind == SpawnKind.Open)
                {
                    started = true;
                    open++;
                    continue;
                }
                if (!started) continue;

                if (kind != SpawnKind.Floor || open < 2) return false;
                if (column.BlockLight(x, y + 1, z) > 0) return false;
                return !SpawnRules.IsSafeBiome(column.Biome(x, y + 1, z));
            }
            return false;
        }

        // A chunk's blocks by column, sections unpacked as they're reached
        private class ChunkColumns
        {
            public int Top { get; }
            public int Bottom { get; }

            private readonly int _lowest;
            private readonly PaletteSection<Block>?[] _blocks;
            private readonly SpawnKind[]?[] _kinds;
            private readonly LightChunk.Section?[] _light;
            private readonly Dictionary<int, PaletteSection<string>> _biomes;
            private readonly Dictionary<object, short[]> _unlocked;

            public ChunkColumns(ConvertedApiChunk chunk)
            {
                IList<PaletteSection<Block>> sections = chunk.BlockState!.Sections;
                int lowest = int.MaxValue, highest = int.MinValue;
                foreach (PaletteSection<Block> section in sections)
                {
                    lowest = Math.Min(lowest, section.Y);
                    highest = Math.Max(highest, section.Y);
                }
                _lowest = lowest;
                Top = highest * 16 + 15;
                Bottom = lowest * 16;

                _blocks = new PaletteSection<Block>?[highest - lowest + 1];
                _kinds = new SpawnKind[]?[_blocks.Length];
                _light = new LightChunk.Section?[_blocks.Length + 1];
                foreach (PaletteSection<Block> section in sections) _blocks[section.Y - lowest] = section;

                if (chunk.BlockLight is LightChunk light)
                {
                    foreach (LightChunk.Section section in light.Sections)
                    {
                        int index = section.Y - lowest;
                        if (index >= 0 && index < _light.Length && section.Values is { Length: >= 2048 }) _light[index] = section;
                    }
                }
                _biomes = chunk.Biome?.Sections.ToDictionary(s => (int)s.Y) ?? [];

                _unlocked = SpawnScanner._unlocked ??= new Dictionary<object, short[]>(ReferenceEqualityComparer.Instance);
                _unlocked.Clear();
            }

            public SpawnKind Kind(int x, int y, int z)
            {
                int index = MathUtilities.FindSectionY(y) - _lowest;
                if (index < 0 || index >= _blocks.Length || _blocks[index] is not PaletteSection<Block> section) return SpawnKind.Open;

                SpawnKind[] kinds = _kinds[index] ??= [.. section.Palette.Select(SpawnRules.Kind)];
                return kinds[Index(section, MathUtilities.FindBlockIndex(x, y, z))];
            }

            // A section with no light saved is dark
            public int BlockLight(int x, int y, int z)
            {
                int index = MathUtilities.FindSectionY(y) - _lowest;
                if (index < 0 || index >= _light.Length || _light[index] is not LightChunk.Section section) return 0;
                return section.GetLight(MathUtilities.FindBlockIndex(x, y, z));
            }

            public string? Biome(int x, int y, int z)
            {
                if (!_biomes.TryGetValue(MathUtilities.FindSectionY(y), out PaletteSection<string>? section) &&
                    !_biomes.TryGetValue(MathUtilities.FindSectionY(y - 1), out section)) return null;
                return section.Palette[Index(section, MathUtilities.FindBiomeIndex(x, y, z))];
            }

            private short Index<T>(PaletteSection<T> section, int index)
            {
                if (section.Type == SectionType.Uniform || section.Locker is null) return 0;

                if (!_unlocked.TryGetValue(section, out short[]? indexes))
                {
                    indexes = new short[section.Locker.UnlockedArrayLength];
                    section.Unlock(indexes);
                    _unlocked[section] = indexes;
                }
                return indexes[index];
            }
        }

        private class Body : IEnumerationBody
        {
            private readonly SpawnScanner _scanner;
            private readonly Action<SpawnRegion> _found;
            private readonly CancellationToken _cancellation;
            private readonly SpawnRegion?[] _regions = new SpawnRegion?[RegionsInParallel];

            public Body(SpawnScanner scanner, Action<SpawnRegion> found, CancellationToken cancellation)
            {
                _scanner = scanner;
                _found = found;
                _cancellation = cancellation;
            }

            public void BeginCycle() => _cancellation.ThrowIfCancellationRequested();
            public void EndCycle() { }

            public void BeginReadingRegion(int index, Coords region) => _regions[index] = new SpawnRegion(region);

            public void EndReadingRegion(int index, Coords region)
            {
                if (_cancellation.IsCancellationRequested || _regions[index] is not SpawnRegion done) return;
                _found(done);
            }

            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk)
            {
                if (_cancellation.IsCancellationRequested || _regions[regionIndex] is not SpawnRegion region) return;
                _scanner.Scan(chunk, region);
            }
        }
    }
}
