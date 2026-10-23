namespace WorldEditor
{
    // Bedrock has no region files. Chunks are found by indexing the world database in the background,
    // then grouped into the same 32x32 regions Java uses so the rest of Mapee can treat them alike
    public class BedrockRegionStore : IChunkSourceRegionStore, IBackgroundRegionStore, IBedrockNeighborSource, IRefreshableRegionStore, IChunkStampRegionStore
    {
        public int Dimension { get; }
        public int Count => Regions.Count;
        public Task Ready => _world?.Indexed ?? Task.CompletedTask;

        public BedrockChunkReader ChunkReader { get; set; } = new();
        public ILogger<ChunkError> ErrorLogger { get; set; } = new ConsoleWriteLogger<ChunkError>();

        private BedrockWorld? _world;

        // Light needs each chunk's neighbours, so recently decoded chunks are kept for reuse
        private const int CACHED_CHUNKS = 1024;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Coords, BlockStateChunk> _blocks = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<Coords> _blockOrder = new();

        private IReadOnlyDictionary<Coords, List<Coords>> Regions => _world?.GetRegions(Dimension) ?? new Dictionary<Coords, List<Coords>>();

        public BedrockRegionStore(int dimension)
        {
            Dimension = dimension;
        }

        // directory is the world folder, the one holding level.dat and db. Returns at once;
        // the returned regions wait for indexing when enumerated
        public IEnumerable<Coords> Itemize(string directory)
        {
            _world = File.Exists(Path.Combine(directory, "db", "CURRENT")) ? BedrockWorld.Open(directory) : null;
            return ItemizeWhenReady();
        }
        private IEnumerable<Coords> ItemizeWhenReady()
        {
            foreach (Coords region in Regions.Keys) yield return region;
        }

        public bool Exists(Coords coords)
        {
            return Regions.ContainsKey(coords);
        }

        public string Stamp() => _world?.CurrentStamp() ?? string.Empty;

        public IReadOnlyDictionary<Coords, ulong> ChunkStamps() => _world?.ChunkStamps(Dimension) ?? new Dictionary<Coords, ulong>();

        public RegionChanges? Refresh()
        {
            if (_world?.Reopen() is not { } next) return null;
            _world = next.World;

            List<Coords> chunks = [.. next.Changed.Where(chunk => chunk.Dimension == Dimension).Select(chunk => new Coords(chunk.X, chunk.Z))];
            if (chunks.Count == 0) return null;

            HashSet<Coords> regions = [];
            foreach (Coords chunk in chunks)
            {
                _blocks.TryRemove(chunk, out _);

                // Light reaches across chunk edges, so a chunk on a region's edge changes the region beside it too
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        regions.Add(new Coords((chunk.X + dx) >> 5, (chunk.Z + dz) >> 5));
                    }
                }
            }
            return new RegionChanges(regions, chunks.Count);
        }

        public bool GetData(Coords coords, out byte[]? buffer, out StorageFormat format)
        {
            buffer = null;
            format = StorageFormat.Bedrock;
            return false;
        }

        public ulong EmitterMask(int x, int z)
        {
            if (_world is null || !_world.ChunkExists(Dimension, x, z)) return 0;
            return _world.EmitterMasks.GetOrAdd((Dimension, x, z), _ =>
            {
                BlockStateChunk? blocks = ReadBlocks(x, z);
                return blocks is null ? 0 : BedrockLight.EmitterMask(blocks, Dimension);
            });
        }
        public void SetEmitterMask(int x, int z, ulong mask)
        {
            _world?.EmitterMasks.TryAdd((Dimension, x, z), mask);
        }
        public BlockStateChunk? ReadBlocks(int x, int z)
        {
            if (_world is null || !_world.ChunkExists(Dimension, x, z)) return null;

            Coords coords = new(x, z);
            if (_blocks.TryGetValue(coords, out BlockStateChunk? cached)) return cached;

            IDictionary<byte[], byte[]> records = _world.Database.GetByPrefix(BedrockKey.ChunkPrefix(x, z, Dimension));
            BlockStateChunk? blocks = ChunkReader.ReadBlocks(x, z, Dimension, records, out _);
            if (blocks is not null) Cache(coords, blocks);
            return blocks;
        }
        private void Cache(Coords coords, BlockStateChunk blocks)
        {
            if (!_blocks.TryAdd(coords, blocks)) return;

            _blockOrder.Enqueue(coords);
            while (_blockOrder.Count > CACHED_CHUNKS && _blockOrder.TryDequeue(out Coords old))
            {
                _blocks.TryRemove(old, out _);
            }
        }

        public IChunk? ReadChunk(Coords coords)
        {
            if (_world is null || !_world.Indexed.IsCompleted || !_world.ChunkExists(Dimension, coords.X, coords.Z)) return null;

            IDictionary<byte[], byte[]> records = _world.Database.GetByPrefix(BedrockKey.ChunkPrefix(coords.X, coords.Z, Dimension));
            return ChunkReader.Read(coords.X, coords.Z, Dimension, records, this, _blocks.GetValueOrDefault(coords));
        }

        public void EnumerateChunks(Coords region, int tasks, Action<int, IChunk> body)
        {
            if (_world is null || !Regions.TryGetValue(region, out List<Coords>? unordered)) return;
            LevelDbReader db = _world.Database;

            // Row by row, so a chunk's neighbours are usually still cached from the previous rows
            List<Coords> chunks = [.. unordered.OrderBy(c => c.Z).ThenBy(c => c.X)];

            ParallelUtilities.BufferedFor(0, chunks.Count, tasks, (index, r) =>
            {
                Coords coords = chunks[index];
                try
                {
                    IDictionary<byte[], byte[]> records = db.GetByPrefix(BedrockKey.ChunkPrefix(coords.X, coords.Z, Dimension));
                    ApiChunk? chunk = ChunkReader.Read(coords.X, coords.Z, Dimension, records, this, _blocks.GetValueOrDefault(coords));
                    if (chunk is not null) body(r, chunk);
                }
                catch (Exception e)
                {
                    ErrorLogger.Log(new ChunkError(e, coords));
                }
            });
        }
    }
}
