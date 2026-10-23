using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;

namespace Mapee.Tests.Find
{
    // Nether portals: how they're grouped, where each leads, and links that don't lead back
    public class PortalMapTests
    {
        private sealed class BlockAPixel(double left, double top) : IMarkupView
        {
            public string Dimension => WorldEditor.Dimension.Overworld.Namespace;
            public (double X, double Y) ToScreen(double x, double z) => (x - left, z - top);
            public (double X, double Z) ToWorld(double x, double y) => (left + x, top + y);
        }

        // The purple inside a frame two wide and three high, along x
        private static IEnumerable<BlockMatch> Portal(int x, int y, int z)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                for (int dx = 0; dx < 2; dx++) yield return new BlockMatch(x + dx, y + dy, z, 0);
            }
        }

        // A in the Overworld and N in the Nether are a pair. B, 100 blocks from A, is near enough N to lead there, but
        // N leads back to A. C, far off, has nothing in the Nether near it
        private static PortalMap Map() => new(
            [.. Portal(800, 64, 0), .. Portal(900, 64, 0), .. Portal(2000, 64, 2000)],
            [.. Portal(100, 70, 0)]);

        [Fact]
        public void GroupsBlocksIntoPortals()
        {
            PortalMap map = Map();

            Assert.Equal(3, map.Overworld.Count);
            Portal a = map.Overworld[0];
            Assert.Equal((6, 64, 66, 0), (a.Blocks, a.MinY, a.MaxY, a.Z));
            Assert.InRange(a.X, 800, 801);
            Assert.Single(map.Nether);
            Assert.Equal(Dimension.Nether, map.Nether[0].Dimension);
        }

        [Fact]
        public void LinksEachToTheNearestInTheOtherDimension()
        {
            PortalMap map = Map();
            Portal a = map.Overworld[0], b = map.Overworld[1], c = map.Overworld[2], n = map.Nether[0];

            PortalLink fromA = map.Link(a);
            Assert.Equal((100, 64, 0), (fromA.TargetX, fromA.TargetY, fromA.TargetZ));
            Assert.Equal(n, fromA.To);
            Assert.Equal(a, fromA.Back);
            Assert.False(fromA.Mismatched);

            Assert.Equal(a, map.Link(n).To);

            // B's way into the Nether lands at N, whose way out is A's
            PortalLink fromB = map.Link(b);
            Assert.Equal(n, fromB.To);
            Assert.Equal(a, fromB.Back);
            Assert.True(fromB.Mismatched);

            // The game would build C a new portal
            PortalLink fromC = map.Link(c);
            Assert.Null(fromC.To);
            Assert.Equal((250, 250), (fromC.TargetX, fromC.TargetZ));
            Assert.False(fromC.Mismatched);
        }

        [Fact]
        public void LooksOnlyWithinTheSearchSquare()
        {
            // 17 blocks off in the Nether is too far; 128 off in the Overworld isn't
            PortalMap map = new([.. Portal(0, 64, 0)], [.. Portal(17, 64, 0)]);
            Assert.Null(map.Link(map.Overworld[0]).To);

            PortalMap back = new([.. Portal(8 * 17 + 4 + 120, 64, 0)], [.. Portal(17, 64, 0)]);
            Assert.Equal(back.Overworld[0], back.Link(back.Nether[0]).To);
        }

        [Fact]
        public void FramesPortalsWithLabelsApart()
        {
            // Two Overworld portals 4 blocks apart, at a block a pixel: one paired, one mismatched
            PortalMap map = new([.. Portal(800, 64, 0), .. Portal(804, 64, 0)], [.. Portal(100, 70, 0)]);
            PortalFrame frame = PortalFrame.Build(map, Dimension.Overworld, new BlockAPixel(700, -100), 400, 400);

            Assert.Equal(2, frame.Portals.Count);
            Assert.Equal([PortalState.Paired, PortalState.Mismatched], frame.Portals.Select(p => p.State));
            Assert.StartsWith("→ Nether 10", frame.Portals[0].Label);
            Assert.StartsWith("which leads back to 80", frame.Portals[1].Detail);
            Assert.True(frame.Portals[1].LabelY - frame.Portals[0].LabelY >= 20);
            Assert.NotNull(frame.Portals[0].Partner);
        }

        [Fact]
        public void ScalesAcrossDimensions()
        {
            Assert.Equal((12, -5), PortalMap.Across(Dimension.Overworld, 100, -33));
            Assert.Equal((800, -264), PortalMap.Across(Dimension.Nether, 100, -33));
        }

        [Fact]
        public void FindsPortalsInAWorld()
        {
            Dictionary<string, object> Section(int y, Func<int, int, int, int> indexAt) => new()
            {
                ["Y"] = unchecked((byte)y),
                ["block_states"] = new Dictionary<string, object>
                {
                    ["palette"] = new List<object> { new Dictionary<string, object> { ["Name"] = "minecraft:air" }, new Dictionary<string, object> { ["Name"] = "minecraft:nether_portal" } },
                    ["data"] = JavaTestWorld.PackBlockStates(2, indexAt)
                },
                ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:plains" } }
            };
            Dictionary<string, object> Chunk(int x, int z, int y, Func<int, int, int, int> indexAt) => new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = new List<object> { Section(y, indexAt) }
            };

            // In the Overworld at 2..3, 64..66, 5; in the Nether at 0..1, 64..66, 0
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0, 4, (x, y, z) => x is 2 or 3 && y <= 2 && z == 5 ? 1 : 0))
                .Chunk(0, 0, Chunk(0, 0, 4, (x, y, z) => x <= 1 && y <= 2 && z == 0 ? 1 : 0), dimension: "the_nether")
                .Save("Portal test");

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            PortalMap map = PortalMap.Read(new SceneInfo(level, Dimension.Overworld), new SceneInfo(level, Dimension.Nether));

            Portal overworld = Assert.Single(map.Overworld), nether = Assert.Single(map.Nether);
            Assert.Equal((6, 64, 5), (overworld.Blocks, overworld.MinY, overworld.Z));
            Assert.Equal(nether, map.Link(overworld).To);
            Assert.Equal(overworld, map.Link(nether).To);
        }
    }
}
