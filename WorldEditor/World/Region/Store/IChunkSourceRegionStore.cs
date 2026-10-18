namespace WorldEditor
{
    // A region store that reads chunks itself instead of handing out region file bytes
    public interface IChunkSourceRegionStore : IRegionStore
    {
        void EnumerateChunks(Coords region, int tasks, Action<int, IChunk> body);

        // One chunk on its own, or null if the world hasn't got it
        IChunk? ReadChunk(Coords chunk);
    }
}
