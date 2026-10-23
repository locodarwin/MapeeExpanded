namespace WorldEditor
{
    // A store that can say, for each chunk, something that changes whenever the game saves it, so an export made
    // earlier can tell which parts of a dimension to draw again. Java has each chunk's save time and place in its
    // region file's header; Bedrock numbers every write. Unchanged numbers mean nothing was saved there since
    public interface IChunkStampRegionStore : IRegionStore
    {
        // Chunk coordinates to their stamp, for every chunk the store has. Reads only headers or keys, never chunks
        IReadOnlyDictionary<Coords, ulong> ChunkStamps();
    }
}
