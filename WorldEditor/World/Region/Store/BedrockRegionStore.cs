namespace WorldEditor
{
    // Bedrock has no region files. Chunks are found by indexing the world database in the background,
    // then grouped into the same 32x32 regions Java uses so the rest of Mapee can treat them alike
    public class BedrockRegionStore : IChunkSourceRegionStore, IBackgroundRegionStore
    {
        public int Dimension { get; }
        public int Count => Regions.Count;
        public Task Ready => _world?.Indexed ?? Task.CompletedTask;

        public BedrockChunkReader ChunkReader { get; set; } = new();
        public ILogger<ChunkError> ErrorLogger { get; set; } = new ConsoleWriteLogger<ChunkError>();

        private BedrockWorld? _world;

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

        public bool GetData(Coords coords, out byte[]? buffer, out StorageFormat format)
        {
            buffer = null;
            format = StorageFormat.Bedrock;
            return false;
        }

        public void EnumerateChunks(Coords region, int tasks, Action<int, IChunk> body)
        {
            if (_world is null || !Regions.TryGetValue(region, out List<Coords>? chunks)) return;
            LevelDbReader db = _world.Database;

            ParallelUtilities.BufferedFor(0, chunks.Count, tasks, (index, r) =>
            {
                Coords coords = chunks[index];
                try
                {
                    IDictionary<byte[], byte[]> records = db.GetByPrefix(BedrockKey.ChunkPrefix(coords.X, coords.Z, Dimension));
                    ApiChunk? chunk = ChunkReader.Read(coords.X, coords.Z, Dimension, records);
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
