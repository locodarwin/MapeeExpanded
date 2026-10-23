using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;

namespace Mapee.Tests.App
{
    // The world the Windows smoke test opens in CI. With MAPEE_SMOKE_WORLD set, it's written there and kept
    public class SmokeWorldTests
    {
        [Fact]
        public void SmokeWorldLoadsAndHasThingsToFind()
        {
            string? keep = Environment.GetEnvironmentVariable("MAPEE_SMOKE_WORLD");
            using JavaTestWorld world = SmokeWorld.Write(string.IsNullOrEmpty(keep) ? null : Path.GetFullPath(keep));

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            Assert.Equal(SmokeWorld.Name, level.WorldName);
            Assert.Equal(4 * SmokeWorld.Radius * SmokeWorld.Radius, WorldLoader.LoadChunks(level, Dimension.Overworld).Count);

            BlockQuery query = new([FindGroups.Find("diamonds")!, FindGroups.Find("spawners")!, FindGroups.Find("chests")!]);
            SceneInfo scene = new(level, Dimension.Overworld);
            FindResults results = new(query);
            Assert.True(new BlockFinder(query).Search(scene.RegionStore, [.. scene.Regions], results.Set));
            List<BlockMatch> found = [.. results.All()];

            Assert.Contains(found, m => m.Target == 0);
            Assert.Equal((8, 50, 8), found.Where(m => m.Target == 1).Select(m => (m.X, m.Y, m.Z)).Single());
            Assert.Equal((20, 50, 4), found.Where(m => m.Target == 2).Select(m => (m.X, m.Y, m.Z)).Single());
        }
    }
}
