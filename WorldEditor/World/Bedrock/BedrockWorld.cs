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

        // Which sections of each chunk hold light sources, filled in as chunks are read; see BedrockLight
        public System.Collections.Concurrent.ConcurrentDictionary<(int Dimension, int X, int Z), ulong> EmitterMasks { get; } = new();

        // The files as they were when this view was opened
        public string Stamp { get; }

        private readonly Dictionary<int, Dictionary<Coords, List<Coords>>> _regions = [];
        private readonly HashSet<(int Dimension, int X, int Z)> _chunks = [];

        // A newer view made from this one, and what changed between them, so each dimension's store can move on to it
        private (BedrockWorld World, IReadOnlyCollection<(int Dimension, int X, int Z)> Changed)? _next;

        private static readonly Dictionary<string, WeakReference<BedrockWorld>> _open = [];

        private BedrockWorld(string directory, string stamp)
        {
            Directory = directory;
            Stamp = stamp;
            Database = new LevelDbReader(Path.Combine(directory, "db"));
            Indexed = Task.Run(Index);
        }

        // A newer view of the same files: the old index plus the chunks written since, without reading the rest
        private BedrockWorld(BedrockWorld older, string stamp, LevelDbReader database, List<(int Dimension, int X, int Z)> written)
        {
            Directory = older.Directory;
            Stamp = stamp;
            Database = database;

            foreach (var (dimension, regions) in older._regions)
            {
                _regions[dimension] = regions.ToDictionary(region => region.Key, region => new List<Coords>(region.Value));
            }
            _chunks = [.. older._chunks];
            foreach (var chunk in written) Add(chunk.Dimension, chunk.X, chunk.Z);

            HashSet<(int, int, int)> changed = [.. written];
            foreach (var (chunk, mask) in older.EmitterMasks)
            {
                if (!changed.Contains(chunk)) EmitterMasks[chunk] = mask;
            }
            Indexed = Task.CompletedTask;
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

                world = new BedrockWorld(directory, DatabaseStamp(directory));
                _open[key] = new WeakReference<BedrockWorld>(world);
                return world;
            }
        }

        // The world as it is now if Minecraft has written to it since this view was opened, with the chunks (of every
        // dimension) written since; null if the files are unchanged. Stores of other dimensions asking later get the
        // same newer view. Throws IOException or UnauthorizedAccessException if the database can't be read
        public (BedrockWorld World, IReadOnlyCollection<(int Dimension, int X, int Z)> Changed)? Reopen()
        {
            Indexed.Wait();
            lock (this)
            {
                if (_next is { } next)
                {
                    // Chained: changes since this view are its successor's plus whatever came after that
                    var later = next.World.Reopen();
                    if (later is null) return next;
                    return (later.Value.World, [.. next.Changed.Concat(later.Value.Changed).Distinct()]);
                }

                string stamp = DatabaseStamp(Directory);
                if (stamp == Stamp) return null;

                LevelDbReader database = new(Path.Combine(Directory, "db"));
                List<(int Dimension, int X, int Z)> changed = [];
                List<(int Dimension, int X, int Z)> written = [];
                database.ForEachChangeSince(Database, key => BedrockKey.TryParse(key, out _), key =>
                {
                    BedrockKey.TryParse(key, out BedrockKey parsed);
                    changed.Add((parsed.Dimension, parsed.X, parsed.Z));
                    if (IsVersionKey(key)) written.Add((parsed.Dimension, parsed.X, parsed.Z));
                });

                BedrockWorld world = new(this, stamp, database, [.. written.Distinct()]);
                _next = (world, [.. changed.Distinct()]);
                lock (_open)
                {
                    _open[$"{Path.GetFullPath(Directory)}|{stamp}"] = new WeakReference<BedrockWorld>(world);
                }
                return _next;
            }
        }

        public string CurrentStamp() => DatabaseStamp(Directory);

        // Waits for indexing. Call from a background thread; the UI should check Indexed.IsCompleted
        public IReadOnlyDictionary<Coords, List<Coords>> GetRegions(int dimension)
        {
            Indexed.Wait();
            return _regions.TryGetValue(dimension, out var regions) ? regions : new Dictionary<Coords, List<Coords>>();
        }

        public bool ChunkExists(int dimension, int x, int z)
        {
            Indexed.Wait();
            return _chunks.Contains((dimension, x, z));
        }

        private void Index()
        {
            try
            {
                // Only the per-chunk Version record is needed to know a chunk exists
                Database.ForEachKey(IsVersionKey, key =>
                {
                    if (BedrockKey.TryParse(key, out BedrockKey parsed)) Add(parsed.Dimension, parsed.X, parsed.Z);
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"Couldn't index Bedrock world {Directory}: {e}");
            }
        }

        private void Add(int dimension, int x, int z)
        {
            if (!_chunks.Add((dimension, x, z))) return;

            if (!_regions.TryGetValue(dimension, out var regions)) _regions[dimension] = regions = [];
            Coords region = new(x >> 5, z >> 5);
            if (!regions.TryGetValue(region, out List<Coords>? chunks)) regions[region] = chunks = [];
            chunks.Add(new Coords(x, z));
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
