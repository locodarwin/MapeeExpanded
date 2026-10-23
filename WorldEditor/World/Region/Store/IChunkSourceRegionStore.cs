namespace WorldEditor
{
    // A region store that reads chunks itself instead of handing out region file bytes
    public interface IChunkSourceRegionStore : IRegionStore
    {
        // Without light, chunks come without the light a Bedrock world has to have worked out, which is most of the
        // cost of reading one; for a search that only looks at blocks. Without sky light, only block light is worked
        // out, for a look at what's lit by torches and lava
        void EnumerateChunks(Coords region, int tasks, Action<int, IChunk> body, bool light = true, bool skyLight = true);

        // One chunk on its own, or null if the world hasn't got it
        IChunk? ReadChunk(Coords chunk);
    }
}
