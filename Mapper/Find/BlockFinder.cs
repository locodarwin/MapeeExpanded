using WorldEditor;

namespace Mapper
{
    // One thing to look for: a single block, or a group found as one, such as an ore in stone and in deepslate. Blocks
    // are named as Java names them, which Bedrock worlds are translated to
    public record FindTarget(string Label, IReadOnlyList<string> Blocks)
    {
        public static FindTarget Of(string block)
        {
            string name = BlockFinder.FullName(block);
            return new FindTarget(ColumnInspector.DisplayName(name), [name]);
        }
    }

    // What to look for, and between which heights (both included; null for no limit)
    public record BlockQuery(IReadOnlyList<FindTarget> Targets, int? MinY = null, int? MaxY = null);

    // A block found, and which of the query's targets it is
    public readonly record struct BlockMatch(int X, int Y, int Z, int Target);

    // Ready-made searches for what people most often look for, each by the name mapee-cli takes for it
    public static class FindGroups
    {
        public static readonly IReadOnlyList<(string Id, FindTarget Target)> Named =
        [
            ("diamonds", new("Diamonds", Names("diamond_ore", "deepslate_diamond_ore"))),
            ("ancient_debris", new("Ancient debris", Names("ancient_debris"))),
            ("spawners", new("Spawners", Names("spawner", "trial_spawner"))),
            ("chests", new("Chests and barrels", Names("chest", "trapped_chest", "barrel",
                "copper_chest", "exposed_copper_chest", "weathered_copper_chest", "oxidized_copper_chest",
                "waxed_copper_chest", "waxed_exposed_copper_chest", "waxed_weathered_copper_chest", "waxed_oxidized_copper_chest"))),
            ("emeralds", new("Emeralds", Names("emerald_ore", "deepslate_emerald_ore"))),
            ("buried", new("Buried things", Names("suspicious_sand", "suspicious_gravel")))
        ];

        public static IReadOnlyList<FindTarget> All { get; } = [.. Named.Select(n => n.Target)];

        public static FindTarget? Find(string id) => Named.FirstOrDefault(n => string.Equals(n.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)).Target;

        private static string[] Names(params string[] names) => [.. names.Select(BlockFinder.FullName)];
    }

    // The blocks Mapee knows, by the names the map uses: every block its default colours cover. Read from the
    // Resources folder on first use
    public static class KnownBlocks
    {
        private static readonly Lazy<IReadOnlyList<string>> _names = new(Read);

        public static IReadOnlyList<string> Names => _names.Value;

        public static bool Contains(string block) => Names.Contains(BlockFinder.FullName(block));

        private static IReadOnlyList<string> Read()
        {
            SortedSet<string> names = new(StringComparer.Ordinal);
            string folder = Path.Combine("Resources", "Mapper", "DefaultAsset", "BlockColor");
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, "*.json"))
                {
                    try
                    {
                        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                        foreach (System.Text.Json.JsonElement entry in document.RootElement.EnumerateArray())
                        {
                            if (entry.ValueKind == System.Text.Json.JsonValueKind.Array && entry.GetArrayLength() > 0 && entry[0].GetString() is string name) names.Add(name);
                        }
                    }
                    catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException)
                    {
                        Console.WriteLine($"Couldn't read the block names in {file}: {e.Message}");
                    }
                }
            }
            foreach (FindTarget group in FindGroups.All) names.UnionWith(group.Blocks);
            return [.. names];
        }
    }

    // Reads a dimension's chunks for their blocks alone and picks out the ones a query asks for. A section whose
    // palette holds none of them is passed over without unpacking, so a search for something rare costs little more
    // than reading the world
    public class BlockFinder
    {
        // Past this many, a search stops: the query is too common a block to show usefully
        public const int MaxMatches = 1_000_000;

        private const int RegionsInParallel = 4;
        private const int ChunksInParallel = 4;

        public BlockQuery Query { get; }

        // How many matches a search stops at
        public int Limit { get; init; } = MaxMatches;

        private readonly Dictionary<string, int> _targetOf = [];
        private readonly IVersionConverter _converter = new ApiChunkVersionConverter();

        [ThreadStatic] private static short[]? _indexes;

        public BlockFinder(BlockQuery query)
        {
            Query = query;
            for (int i = 0; i < query.Targets.Count; i++)
            {
                foreach (string block in query.Targets[i].Blocks) _targetOf.TryAdd(FullName(block), i);
            }
        }

        // "diamond_ore" is "minecraft:diamond_ore"; names with a namespace stay as they are
        public static string FullName(string block)
        {
            block = block.Trim().ToLowerInvariant();
            return block.Contains(':') ? block : "minecraft:" + block;
        }

        // Searches the regions a few at a time. found is called once for each region, with what it holds (perhaps
        // nothing), from whichever thread read it. Returns false if it stopped at Limit
        public bool Search(IRegionStore store, IReadOnlyList<Coords> regions, Action<Coords, IReadOnlyList<BlockMatch>> found, CancellationToken cancellation = default)
        {
            ApiChunkLoadOptions options = new() { KeepDataTag = false };
            options.BlockStateOptions.KeepDataTag = false;
            foreach (ObjectLoadOptions skipped in new[] { options.BiomeOptions, options.BlockLightOptions, options.SkyLightOptions, options.HeightmapOptions }) skipped.LoadObject = false;

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => store,
                new ChunkEnumeratorFromRegionFactory(_ => new ApiChunkReader() { LoadOptions = options })) { Light = false };

            Body body = new(this, found, cancellation);
            Coords[] all = [.. regions];
            try
            {
                enumerator.Enumerate(all, body);
            }
            catch (OperationCanceledException) when (body.Full && !cancellation.IsCancellationRequested)
            {
            }
            cancellation.ThrowIfCancellationRequested();
            return !body.Full;
        }

        // Every match in one chunk, as the map would see its blocks (converted to the newest names)
        public void Search(IChunk chunk, List<BlockMatch> into)
        {
            ConvertedApiChunk? converted = chunk is ApiChunk api ? _converter.Convert(api, api.Version, WorldEditor.Version.Newest, UsageIntent.Read) as ConvertedApiChunk : chunk as ConvertedApiChunk;
            if (converted?.BlockState is not BlockStateChunk blocks) return;

            int minY = Query.MinY ?? int.MinValue, maxY = Query.MaxY ?? int.MaxValue;
            int left = chunk.X * 16, top = chunk.Z * 16;

            for (int s = 0; s < blocks.Sections.Count; s++)
            {
                PaletteSection<Block>? section = blocks.Sections[s];
                if (section is null) continue;

                int bottom = section.Y * 16;
                if (bottom + 15 < minY || bottom > maxY) continue;

                int[]? targets = Targets(section.Palette);
                if (targets is null) continue;

                if (section.Type == SectionType.Uniform || section.Locker is null)
                {
                    if (targets[0] < 0) continue;
                    for (int i = 0; i < 4096; i++) Add(into, left, bottom, top, i, targets[0], minY, maxY);
                    continue;
                }

                short[] indexes = _indexes ??= new short[4096];
                section.Unlock(indexes);
                for (int i = 0; i < 4096; i++)
                {
                    int index = indexes[i];
                    if ((uint)index < (uint)targets.Length && targets[index] >= 0) Add(into, left, bottom, top, i, targets[index], minY, maxY);
                }
            }
        }

        // For each palette entry, the target it is, or -1; null when the palette holds none
        private int[]? Targets(Block[] palette)
        {
            int[]? output = null;
            for (int i = 0; i < palette.Length; i++)
            {
                if (palette[i].Name is not string name || !_targetOf.TryGetValue(name, out int target)) continue;
                if (output is null)
                {
                    output = new int[palette.Length];
                    Array.Fill(output, -1);
                }
                output[i] = target;
            }
            return output;
        }

        // Blocks are numbered x, then z, then y, within a section (MathUtilities.FindBlockIndex)
        private static void Add(List<BlockMatch> into, int left, int bottom, int top, int index, int target, int minY, int maxY)
        {
            int y = bottom + (index >> 8);
            if (y < minY || y > maxY) return;
            into.Add(new BlockMatch(left + (index & 15), y, top + ((index >> 4) & 15), target));
        }

        private class Body : IEnumerationBody
        {
            private readonly BlockFinder _finder;
            private readonly Action<Coords, IReadOnlyList<BlockMatch>> _found;
            private readonly CancellationToken _cancellation;
            private readonly List<BlockMatch>[] _regions = new List<BlockMatch>[RegionsInParallel];
            private int _total;

            public bool Full => _total > _finder.Limit;

            public Body(BlockFinder finder, Action<Coords, IReadOnlyList<BlockMatch>> found, CancellationToken cancellation)
            {
                _finder = finder;
                _found = found;
                _cancellation = cancellation;
            }

            public void BeginCycle()
            {
                if (Full) throw new OperationCanceledException();
                _cancellation.ThrowIfCancellationRequested();
            }
            public void EndCycle() { }

            public void BeginReadingRegion(int index, Coords region) => _regions[index] = [];

            public void EndReadingRegion(int index, Coords region)
            {
                // Chunks are read in parallel; sorted, a search finds the same list every time
                List<BlockMatch> matches = _regions[index];
                matches.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
                _found(region, matches);
            }

            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk)
            {
                if (_cancellation.IsCancellationRequested || Full) return;

                List<BlockMatch> found = [];
                _finder.Search(chunk, found);
                if (found.Count == 0) return;

                // Counted as found, so a search for a common block stops within a region rather than after it
                Interlocked.Add(ref _total, found.Count);
                List<BlockMatch> region = _regions[regionIndex];
                lock (region) region.AddRange(found);
            }
        }
    }
}
