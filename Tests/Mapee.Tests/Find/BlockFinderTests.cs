using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;
using static Mapee.Tests.Fixtures.BedrockRecords;

namespace Mapee.Tests.Find
{
    // The block finder: what it picks out of Java and Bedrock chunks, heights, the limit, veins and where marks go
    public class BlockFinderTests
    {
        private static Dictionary<string, object> JavaSection(int y, string[] palette, Func<int, int, int, int> indexAt)
        {
            List<object> entries = [.. palette.Select(name => (object)new Dictionary<string, object> { ["Name"] = name })];
            Dictionary<string, object> states = new() { ["palette"] = entries };
            if (palette.Length > 1) states["data"] = JavaTestWorld.PackBlockStates(palette.Length, indexAt);
            return new()
            {
                ["Y"] = unchecked((byte)y),
                ["block_states"] = states,
                ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:plains" } }
            };
        }

        private static Dictionary<string, object> JavaChunk(int x, int z, params Dictionary<string, object>[] sections)
        {
            return new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = sections.Cast<object>().ToList()
            };
        }

        // Chunk 0, 0: a vein of three diamonds touching by edges, and one on its own. Chunk 1, 0: a chest.
        // Chunk -1, -1 (another region): a deepslate diamond deep down
        private static JavaTestWorld JavaWorld()
        {
            string[] ores = ["minecraft:air", "minecraft:diamond_ore"];
            return new JavaTestWorld()
                .Chunk(0, 0, JavaChunk(0, 0, JavaSection(4, ores, (x, y, z) => (x, y, z) is (3, 0, 3) or (4, 1, 3) or (4, 1, 4) or (10, 0, 10) ? 1 : 0)))
                .Chunk(1, 0, JavaChunk(1, 0, JavaSection(4, ["minecraft:air", "minecraft:chest"], (x, y, z) => (x, y, z) == (0, 6, 0) ? 1 : 0)))
                .Chunk(-1, -1, JavaChunk(-1, -1, JavaSection(-4, ["minecraft:stone", "minecraft:deepslate_diamond_ore"], (x, y, z) => (x, y, z) == (15, 4, 15) ? 1 : 0)))
                .Save("Find test");
        }

        private static (FindResults Results, bool Complete) Search(string levelDat, BlockQuery query, int limit = BlockFinder.MaxMatches)
        {
            SceneInfo scene = new(WorldLoader.ReadLevel(levelDat), Dimension.Overworld);
            FindResults results = new(query);
            bool complete = new BlockFinder(query) { Limit = limit }.Search(scene.RegionStore, [.. scene.Regions], results.Set);
            return (results, complete);
        }

        private static readonly FindTarget Diamonds = FindGroups.All.Single(g => g.Label == "Diamonds");
        private static readonly FindTarget Chests = FindGroups.All.Single(g => g.Label == "Chests and barrels");

        [Fact]
        public void FindsBlocksInJavaChunksAndMergesVeins()
        {
            using JavaTestWorld world = JavaWorld();
            var (results, complete) = Search(world.LevelDat, new BlockQuery([Diamonds, Chests]));

            Assert.True(complete);
            Assert.Equal(6, results.Total);
            Assert.Equal([5, 1], results.Counts);

            List<FoundCluster> clusters = [.. results.Clusters.OrderBy(c => c.Y).ThenBy(c => c.X)];
            Assert.Equal(4, clusters.Count);
            Assert.Equal(new FoundCluster(0, 1, -1, -60, -1, -60, -60), clusters[0]);
            Assert.Equal(new FoundCluster(0, 1, 10, 64, 10, 64, 64), clusters[1]);
            Assert.Equal(new FoundCluster(1, 1, 16, 70, 0, 70, 70), clusters[3]);

            // The vein is named by one of its blocks, the one nearest its middle
            FoundCluster vein = clusters[2];
            Assert.Equal((0, 3, 64, 65), (vein.Target, vein.Count, vein.MinY, vein.MaxY));
            Assert.Equal((4, 65, 3), (vein.X, vein.Y, vein.Z));
        }

        [Fact]
        public void KeepsToTheHeightsAsked()
        {
            using JavaTestWorld world = JavaWorld();
            var (results, _) = Search(world.LevelDat, new BlockQuery([Diamonds], MinY: 0, MaxY: 64));

            Assert.Equal(2, results.Total);
            Assert.All(results.Clusters, c => Assert.Equal(64, c.Y));
        }

        [Fact]
        public void FindsBedrockBlocksByTheirJavaNames()
        {
            Dictionary<string, object>[] palette = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:diamond_ore"), Nbt.Block("minecraft:mob_spawner")];
            using BedrockTestWorld world = new BedrockTestWorld()
                .Chunk(0, 0)
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 4), PaletteSubChunk(9, 4, palette, (x, y, z) => (x, y, z) switch
                {
                    (1, 1, 1) => 1,
                    (8, 0, 8) => 2,
                    _ => 0
                }))
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.Data3D), Data3D((x, z) => 64 + 65, 1))
                .Save("Bedrock find test");

            var (results, complete) = Search(world.LevelDat, new BlockQuery([Diamonds, FindGroups.All.Single(g => g.Label == "Spawners")]));

            Assert.True(complete);
            Assert.Equal([new FoundCluster(1, 1, 8, 64, 8, 64, 64), new FoundCluster(0, 1, 1, 65, 1, 65, 65)], results.Clusters.OrderBy(c => c.Y));
        }

        [Fact]
        public void StopsAtTheLimitForACommonBlock()
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, JavaChunk(0, 0, JavaSection(-4, ["minecraft:stone"], (x, y, z) => 0)))
                .Chunk(1, 0, JavaChunk(1, 0, JavaSection(-4, ["minecraft:stone"], (x, y, z) => 0)))
                .Save();

            var (whole, complete) = Search(world.LevelDat, new BlockQuery([FindTarget.Of("stone")]));
            Assert.True(complete);
            Assert.Equal(2 * 4096, whole.Total);

            var (_, stopped) = Search(world.LevelDat, new BlockQuery([FindTarget.Of("stone")]), limit: 1000);
            Assert.False(stopped);
        }

        [Fact]
        public void ARegionSearchedAgainReplacesItsMatches()
        {
            FindResults results = new(new BlockQuery([Diamonds, Chests]));
            results.Set(new Coords(0, 0), [new BlockMatch(1, 10, 1, 0), new BlockMatch(2, 10, 1, 0), new BlockMatch(20, 10, 1, 1)]);
            results.Set(new Coords(1, 0), [new BlockMatch(600, 10, 1, 1)]);

            Assert.Equal(4, results.Total);
            Assert.Equal(3, results.Clusters.Count);
            Assert.Equal([2, 0], results.Chunks[new Coords(0, 0)]);
            Assert.Equal([0, 1], results.Chunks[new Coords(1, 0)]);
            Assert.Equal([0, 1], results.Chunks[new Coords(37, 0)]);

            results.Set(new Coords(0, 0), []);
            Assert.Equal(1, results.Total);
            Assert.Equal([0, 1], results.Counts);
            Assert.Single(results.Clusters);
            Assert.Equal([new BlockMatch(600, 10, 1, 1)], results.MatchesIn(512, 0, 1023, 511));
            Assert.Empty(results.MatchesIn(0, 0, 511, 511));
        }

        [Fact]
        public void VeinsTouchAtCornersButNotAcrossTargets()
        {
            List<FoundCluster> clusters = FindResults.Cluster(
            [
                new BlockMatch(0, 0, 0, 0), new BlockMatch(1, 1, 1, 0),
                new BlockMatch(3, 0, 0, 0),
                new BlockMatch(2, 0, 0, 1)
            ]);
            Assert.Equal([2, 1, 1], clusters.Select(c => c.Count));

            List<FoundCluster> near = FindResults.ByDistance(clusters, 3.5, 0.5);
            Assert.Equal((3, 0), (near[0].X, near[0].Target));
            Assert.Equal((2, 1), (near[1].X, near[1].Target));
        }

        private class View(double left, double top, double zoom) : IMarkupView
        {
            public string Dimension => "minecraft:overworld";
            public (double X, double Y) ToScreen(double x, double z) => ((x - left) * zoom, (z - top) * zoom);
            public (double X, double Z) ToWorld(double x, double y) => (left + x / zoom, top + y / zoom);
        }

        [Fact]
        public void ShowsChunksZoomedOutAndBlocksZoomedIn()
        {
            FindResults results = new(new BlockQuery([Diamonds, Chests]));
            results.Set(new Coords(0, 0), [new BlockMatch(1, 10, 1, 0), new BlockMatch(1, 12, 1, 0), new BlockMatch(5, 10, 1, 0), new BlockMatch(40, 10, 1, 1)]);

            // A block a pixel: a square in the middle of each chunk for each target there, bigger for more, and the
            // rarer target drawn last
            results.Set(new Coords(0, 0), [new BlockMatch(1, 10, 1, 0), new BlockMatch(1, 12, 1, 0), new BlockMatch(5, 10, 1, 0), new BlockMatch(9, 10, 9, 1), new BlockMatch(40, 10, 1, 1)]);
            FindFrame far = FindFrame.Build(results, new View(0, 0, 1), 800, 600);
            Assert.False(far.Dots);
            Assert.Equal([0, 1, 1], far.Marks.Select(m => m.Target));
            FindMark diamonds = far.Marks[0], spawner = far.Marks[1];
            Assert.Equal(16 * FindFrame.Share(3), diamonds.Size);
            Assert.Equal((8.0, 8.0), (diamonds.X + diamonds.Size / 2, diamonds.Y + diamonds.Size / 2));
            Assert.True(diamonds.Size > spawner.Size && diamonds.Opacity > spawner.Opacity);
            Assert.Equal((40.0, 8.0), (far.Marks[2].X + far.Marks[2].Size / 2, far.Marks[2].Y + far.Marks[2].Size / 2));
            results.Set(new Coords(0, 0), [new BlockMatch(1, 10, 1, 0), new BlockMatch(1, 12, 1, 0), new BlockMatch(5, 10, 1, 0), new BlockMatch(40, 10, 1, 1)]);

            // Four pixels a block: a dot for each column found, two heights in one column making one dot; the
            // picked vein is ringed
            FindFrame near = FindFrame.Build(results, new View(0, 0, 4), 100, 100, new FoundCluster(0, 2, 1, 10, 1, 10, 12));
            Assert.Equal([(6.0, 6.0), (22.0, 6.0)], near.Marks.Select(m => (m.X + m.Size / 2, m.Y + m.Size / 2)).Order());
            Assert.Equal((6, 6), (near.Ring!.Value.X, near.Ring.Value.Y));
        }
    }
}
