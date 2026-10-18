namespace WorldEditor
{
    // Reads single chunks, such as the one under the mouse, from either kind of region store. Region files are
    // read whole, so the last one is kept for a few seconds: long enough to move around in, short enough that
    // a world being played in still shows its changes
    public class ChunkLookup
    {
        public IRegionStore RegionStore { get; }
        public IVersionConverter VersionConverter { get; set; } = new ApiChunkVersionConverter();
        public TimeSpan RegionLifetime { get; set; } = TimeSpan.FromSeconds(5);

        private readonly ChunkEnumeratorFromRegion _enumerator = new(1);

        private Coords? _region;
        private byte[]? _buffer;
        private StorageFormat _format;
        private DateTime _readAt;

        public ChunkLookup(IRegionStore regionStore)
        {
            RegionStore = regionStore;

            // Blocks and biomes are all a lookup needs
            ApiChunkLoadOptions options = new() { KeepDataTag = false };
            foreach (ObjectLoadOptions objectOptions in new[] { options.BiomeOptions, options.BlockStateOptions }) objectOptions.KeepDataTag = false;
            foreach (ObjectLoadOptions objectOptions in new[] { options.BlockLightOptions, options.SkyLightOptions, options.HeightmapOptions }) objectOptions.LoadObject = false;
            _enumerator.ChunkReader = new ApiChunkReader() { LoadOptions = options };
        }

        // The chunk converted to the newest version, as the map draws it; null if it doesn't exist or won't read
        public ConvertedApiChunk? Read(Coords chunk)
        {
            if (Find(chunk) is not ApiChunk apiChunk) return null;
            return VersionConverter.Convert(apiChunk, apiChunk.Version, Version.Newest, UsageIntent.Read) as ConvertedApiChunk;
        }

        private IChunk? Find(Coords chunk)
        {
            if (RegionStore is IChunkSourceRegionStore source) return source.ReadChunk(chunk);

            Coords region = new(MathUtilities.FindSectionY(chunk.X, 32), MathUtilities.FindSectionY(chunk.Z, 32));
            if (_region is not Coords cached || !cached.Equals(region) || DateTime.UtcNow - _readAt > RegionLifetime)
            {
                _region = region;
                _readAt = DateTime.UtcNow;
                if (!RegionStore.GetData(region, out _buffer, out _format)) _buffer = null;
            }
            if (_buffer is null) return null;

            IChunk? output = null;
            _enumerator.Enumerate(new ChunkEnumerateFromRegionArgs(_buffer, [chunk], _format), (_, read) => output = read);
            return output;
        }
    }
}
