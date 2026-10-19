using System.IO;
using WorldEditor;

namespace Mapper
{
    public struct SceneInfo
    {
        public Level Level { get; set; }
        public Dimension Dimension { get; set; }
        public IRegionStore RegionStore { get; set; }

        // Every region in the dimension. For a store indexing in the background, enumerating waits for it
        public IEnumerable<Coords> Regions { get; }

        public SceneInfo(Level level, Dimension dimension)
        {
            Level = level;
            Dimension = dimension;
            RegionStore = RegionStoreFactory.CreateRegionStore(Level, Dimension);

            // A Bedrock store reads the whole world database, so it takes the world folder itself
            string regionDirectory = Level.Edition == Edition.Bedrock
                ? Level.Directory ?? string.Empty
                : Path.Combine(Level.Directory, Dimension.GetRegionFolder(Level.Version.Version));
            Regions = RegionStore.Itemize(regionDirectory);
        }
    }
}
