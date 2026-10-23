using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;

namespace Mapee.Tests.Find
{
    // Where mobs can spawn: which blocks are floors, light, room above, height cuts and biomes
    public class SpawnScannerTests
    {
        private static Dictionary<string, object> Entry(string name, params (string Name, string Value)[] properties)
        {
            Dictionary<string, object> entry = new() { ["Name"] = name };
            if (properties.Length > 0) entry["Properties"] = properties.ToDictionary(p => p.Name, p => (object)p.Value);
            return entry;
        }

        // Section 4 (Y 64 to 79), blocks from indexAt and block light from lightAt
        private static Dictionary<string, object> Section(Dictionary<string, object>[] palette, Func<int, int, int, int> indexAt, Func<int, int, int, int> lightAt, string biome = "minecraft:plains")
        {
            Dictionary<string, object> states = new() { ["palette"] = palette.Cast<object>().ToList() };
            if (palette.Length > 1) states["data"] = JavaTestWorld.PackBlockStates(palette.Length, indexAt);

            byte[] light = new byte[2048];
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int index = y * 256 + z * 16 + x, value = lightAt(x, y, z);
                        light[index / 2] |= (byte)(index % 2 == 0 ? value & 15 : (value & 15) << 4);
                    }
                }
            }

            return new()
            {
                ["Y"] = (byte)4,
                ["block_states"] = states,
                ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { biome } },
                ["BlockLight"] = light
            };
        }

        private static Dictionary<string, object> Chunk(int x, int z, Dictionary<string, object> section)
        {
            return new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["isLightOn"] = (byte)1,
                ["sections"] = new List<object> { section }
            };
        }

        private static readonly Dictionary<string, object>[] Palette = [
            Entry("minecraft:air"), Entry("minecraft:grass_block"), Entry("minecraft:glass"), Entry("minecraft:water"),
            Entry("minecraft:oak_slab", ("type", "bottom"), ("waterlogged", "false")), Entry("minecraft:oak_slab", ("type", "top"), ("waterlogged", "false")),
            Entry("minecraft:stone"), Entry("minecraft:short_grass"), Entry("minecraft:white_carpet")
        ];
        private const int Air = 0, Grass = 1, Glass = 2, Water = 3, BottomSlab = 4, TopSlab = 5, Stone = 6, Tuft = 7, Carpet = 8;

        // Chunk 0, 0: a floor at Y 64 of grass, but glass at x 0, water at x 1, a bottom slab at x 2, a top slab at
        // x 3, a carpet over grass at x 4 and a tuft of grass over grass at x 5. Light above the floor from x 8 on.
        // Rows z 8 on have a stone roof at Y 70. Chunk 1, 0: dark grass in a mushroom field
        private static JavaTestWorld World()
        {
            int Block(int x, int y, int z) => (x, y) switch
            {
                (0, 0) => Glass,
                (1, 0) => Water,
                (2, 0) => BottomSlab,
                (3, 0) => TopSlab,
                (4, 1) => Carpet,
                (5, 1) => Tuft,
                (_, 0) => Grass,
                (_, 6) when z >= 8 => Stone,
                _ => Air
            };
            int Light(int x, int y, int z) => x >= 8 && y is >= 1 and <= 5 ? 7 : 0;

            return new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0, Section(Palette, Block, Light)))
                .Chunk(1, 0, Chunk(1, 0, Section(Palette, (x, y, z) => y == 0 ? Grass : Air, (x, y, z) => 0, "minecraft:mushroom_fields")))
                .Save("Spawn test");
        }

        private static SpawnRegion Scan(string levelDat, int? startY = null)
        {
            SceneInfo scene = new(WorldLoader.ReadLevel(levelDat), Dimension.Overworld);
            List<SpawnRegion> found = [];
            new SpawnScanner(startY).Scan(scene.RegionStore, [.. scene.Regions], found.Add);
            return Assert.Single(found);
        }

        [Fact]
        public void MarksDarkFloorsWithRoomAbove()
        {
            using JavaTestWorld world = World();
            SpawnRegion region = Scan(world.LevelDat);

            // Row z 0: no roof
            bool[] row = [.. Enumerable.Range(0, 16).Select(x => region[x, 0])];
            Assert.Equal([false, false, false, true, false, true, true, true, false, false, false, false, false, false, false, false], row);

            // The mushroom field's chunk is as dark, but nothing spawns there
            Assert.False(region[16, 0]);
            Assert.False(region[20, 5]);
        }

        [Fact]
        public void ARoofIsAFloorFromAbove()
        {
            using JavaTestWorld world = World();
            SpawnRegion region = Scan(world.LevelDat);

            // The roof's top is dark, even over the lit part
            Assert.True(region[6, 8]);
            Assert.True(region[12, 8]);
        }

        [Fact]
        public void ACutShowsTheFloorsUnderIt()
        {
            using JavaTestWorld world = World();

            // Under the roof, cut in the open or in the roof itself: the grass, dark on the left and lit on the right
            foreach (int cut in new[] { 69, 70 })
            {
                SpawnRegion region = Scan(world.LevelDat, cut);
                Assert.True(region[6, 8]);
                Assert.False(region[12, 8]);
                Assert.True(region[6, 0]);
            }

            // Cut just above the floor, there's still room for a mob, as there is above the top of the world
            Assert.True(Scan(world.LevelDat, 65)[6, 8]);
        }

        [Theory]
        [InlineData("minecraft:stone", SpawnKind.Floor)]
        [InlineData("minecraft:grass_block", SpawnKind.Floor)]
        [InlineData("minecraft:oak_leaves", SpawnKind.Ledge)]
        [InlineData("minecraft:white_stained_glass", SpawnKind.Ledge)]
        [InlineData("minecraft:cobblestone_wall", SpawnKind.Ledge)]
        [InlineData("minecraft:packed_ice", SpawnKind.Ledge)]
        [InlineData("minecraft:wall_torch", SpawnKind.Open)]
        [InlineData("minecraft:oak_wall_sign", SpawnKind.Open)]
        [InlineData("minecraft:poppy", SpawnKind.Open)]
        [InlineData("minecraft:oak_sapling", SpawnKind.Open)]
        [InlineData("minecraft:stone_pressure_plate", SpawnKind.Open)]
        [InlineData("minecraft:lava", SpawnKind.Water)]
        [InlineData("minecraft:infested_stone", SpawnKind.Floor)]
        [InlineData("minecraft:stone_stairs", SpawnKind.Ledge)]
        public void SortsBlocks(string name, SpawnKind kind)
        {
            Assert.Equal(kind, SpawnRules.Kind(new Block(name)));
        }

        [Fact]
        public void UpsideDownStairsAndWaterloggedPlantsCount()
        {
            Assert.Equal(SpawnKind.Floor, SpawnRules.Kind(new Block("minecraft:stone_stairs", [new Property() { Name = "half", Value = "top" }])));
            Assert.Equal(SpawnKind.Floor, SpawnRules.Kind(new Block("minecraft:stone_slab", [new Property() { Name = "type", Value = "double" }])));
            Assert.Equal(SpawnKind.Water, SpawnRules.Kind(new Block("minecraft:oak_sign", [new Property() { Name = "waterlogged", Value = "true" }])));
        }

        [Fact]
        public void TheNetherHasNothingToLightUp()
        {
            Assert.True(SpawnRules.HasSpawning(Dimension.Overworld));
            Assert.True(SpawnRules.HasSpawning(Dimension.TheEnd));
            Assert.False(SpawnRules.HasSpawning(Dimension.Nether));
        }
    }
}
