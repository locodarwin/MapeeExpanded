namespace WorldEditor
{
    // An open Bedrock world database plus an index of which chunks exist, grouped into 32x32 regions
    // per dimension. Indexing reads the whole database, so it runs in the background and is shared:
    // every dimension's region store for the same world uses one instance while the files are unchanged
    public sealed class BedrockWorld
    {
        public string Directory { get; }
        public LevelDbReader Database { get; }
        public Task Indexed { get; }

        private readonly Dictionary<int, Dictionary<Coords, List<Coords>>> _regions = [];

        private static readonly Dictionary<string, WeakReference<BedrockWorld>> _open = [];

        private BedrockWorld(string directory)
        {
            Directory = directory;
            Database = new LevelDbReader(Path.Combine(directory, "db"));
            Indexed = Task.Run(Index);
        }

        // Reuses an already open world unless Minecraft has written to it since
        public static BedrockWorld Open(string directory)
        {
            string key = $"{Path.GetFullPath(directory)}|{DatabaseStamp(directory)}";
            lock (_open)
            {
                if (_open.TryGetValue(key, out WeakReference<BedrockWorld>? reference) && reference.TryGetTarget(out BedrockWorld? world))
                {
                    return world;
                }

                foreach (string stale in _open.Where(x => !x.Value.TryGetTarget(out _)).Select(x => x.Key).ToList())
                {
                    _open.Remove(stale);
                }

                world = new BedrockWorld(directory);
                _open[key] = new WeakReference<BedrockWorld>(world);
                return world;
            }
        }

        // Waits for indexing. Call from a background thread; the UI should check Indexed.IsCompleted
        public IReadOnlyDictionary<Coords, List<Coords>> GetRegions(int dimension)
        {
            Indexed.Wait();
            return _regions.TryGetValue(dimension, out var regions) ? regions : new Dictionary<Coords, List<Coords>>();
        }

        private void Index()
        {
            try
            {
                // Only the per-chunk Version record is needed to know a chunk exists
                Database.ForEachKey(IsVersionKey, key =>
                {
                    if (!BedrockKey.TryParse(key, out BedrockKey parsed)) return;

                    if (!_regions.TryGetValue(parsed.Dimension, out var regions)) _regions[parsed.Dimension] = regions = [];

                    Coords region = new(parsed.X >> 5, parsed.Z >> 5);
                    if (!regions.TryGetValue(region, out List<Coords>? chunks)) regions[region] = chunks = [];
                    chunks.Add(new Coords(parsed.X, parsed.Z));
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"Couldn't index Bedrock world {Directory}: {e}");
            }
        }

        private static bool IsVersionKey(ReadOnlySpan<byte> key)
        {
            if (key.Length != 9 && key.Length != 13) return false;

            BedrockChunkTag tag = (BedrockChunkTag)key[^1];
            return tag == BedrockChunkTag.Version || tag == BedrockChunkTag.LegacyVersion;
        }

        private static string DatabaseStamp(string directory)
        {
            string db = Path.Combine(directory, "db");
            try
            {
                return string.Join("|", System.IO.Directory.GetFiles(db, "MANIFEST-*").Concat(System.IO.Directory.GetFiles(db, "*.log"))
                    .Select(file => new FileInfo(file))
                    .OrderBy(file => file.Name)
                    .Select(file => $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}"));
            }
            catch (IOException)
            {
                return DateTime.UtcNow.Ticks.ToString();
            }
        }
    }
}
