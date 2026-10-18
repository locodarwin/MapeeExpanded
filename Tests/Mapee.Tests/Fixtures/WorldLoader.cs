using System.Collections.Concurrent;
using WorldEditor;

namespace Mapee.Tests.Fixtures
{
    // Loads a world the way the GUI does: level reader, region store, chunk enumerator, version converter
    public static class WorldLoader
    {
        public static Level ReadLevel(string levelDat)
        {
            Level? level = new AnyEditionLevelReader().Read(levelDat);
            Assert.NotNull(level);
            return level;
        }

        public static IReadOnlyDictionary<Coords, ConvertedApiChunk> LoadChunks(Level level, Dimension dimension)
        {
            IRegionStore store = RegionStoreFactory.CreateRegionStore(level, dimension);
            try
            {
                // As Mapper's SceneInfo does: Bedrock stores take the world folder, Java ones the dimension's region folder
                string directory = level.Edition == Edition.Bedrock ? level.Directory! : Path.Combine(level.Directory!, dimension.GetRegionFolder(level.Version.Version)!);
                Coords[] regions = [.. store.Itemize(directory)];
                CollectingBody body = new();
                new ChunkEnumerator(1, 2, () => store, new ChunkEnumeratorFromRegionFactory(i => new ApiChunkReader())).Enumerate(regions, body);
                return body.Chunks;
            }
            finally
            {
                (store as IDisposable)?.Dispose();
            }
        }

        private class CollectingBody : IEnumerationBody
        {
            public ConcurrentDictionary<Coords, ConvertedApiChunk> Chunks { get; } = new();
            private readonly ApiChunkVersionConverter _converter = new();

            public void BeginCycle() { }
            public void EndCycle() { }
            public void BeginReadingRegion(int index, Coords region) { }
            public void EndReadingRegion(int index, Coords region) { }

            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk)
            {
                if (chunk is ApiChunk api && _converter.Convert(api, api.Version, WorldEditor.Version.Newest, UsageIntent.Read) is ConvertedApiChunk converted)
                {
                    Chunks[new Coords(chunk.X, chunk.Z)] = converted;
                }
            }
        }
    }
}
