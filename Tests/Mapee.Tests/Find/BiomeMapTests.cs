using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;

namespace Mapee.Tests.Find
{
    // Biomes: which one a column has (its surface's, and a cave biome under it; in the Nether, any height), patches
    // joined across chunks and regions, the small ones left out, and where they're shaded on the screen
    public class BiomeMapTests
    {
        // A biome storage of a section: 4 by 4 by 4 cells, packed as Java packs them (YZX, no spanning, at least 1 bit)
        private static Dictionary<string, object> Biomes(string[] palette, Func<int, int, int, int>? indexAt = null)
        {
            List<object> names = [.. palette.Select(p => (object)("minecraft:" + p))];
            if (palette.Length == 1 || indexAt is null) return new() { ["palette"] = names };

            int bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(palette.Length)));
            int perLong = 64 / bits;
            long[] data = new long[(64 + perLong - 1) / perLong];
            for (int y = 0; y < 4; y++)
                for (int z = 0; z < 4; z++)
                    for (int x = 0; x < 4; x++)
                    {
                        int i = y << 4 | z << 2 | x;
                        data[i / perLong] |= (long)indexAt(x, y, z) << (i % perLong * bits);
                    }
            return new() { ["palette"] = names, ["data"] = data };
        }

        private static Dictionary<string, object> Section(int y, Dictionary<string, object> biomes, string block = "stone") => new()
        {
            ["Y"] = unchecked((byte)(sbyte)y),
            ["block_states"] = new Dictionary<string, object> { ["palette"] = new List<object> { new Dictionary<string, object> { ["Name"] = "minecraft:" + block } } },
            ["biomes"] = biomes
        };

        // Every column's first air at height top, as the game keeps WORLD_SURFACE: from the floor at -64, 9 bits each,
        // 7 to a long
        private static long[] Heights(int top)
        {
            long[] output = new long[37];
            for (int i = 0; i < 256; i++) output[i / 7] |= (long)(top + 64) << (i % 7 * 9);
            return output;
        }

        private static Dictionary<string, object> Chunk(int x, int z, int? top, params Dictionary<string, object>[] sections)
        {
            Dictionary<string, object> output = new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = sections.Cast<object>().ToList()
            };
            if (top is int height) output["Heightmaps"] = new Dictionary<string, object> { ["WORLD_SURFACE"] = Heights(height) };
            return output;
        }

        private static DimensionBiomes Read(JavaTestWorld world, Dimension? dimension = null) =>
            BiomeMap.Read(new SceneInfo(WorldLoader.ReadLevel(world.LevelDat), dimension ?? Dimension.Overworld));

        private static string Describe(BiomePatch p) => $"{p.Name} {p.Cells} at {p.X},{p.Z}";

        [Fact]
        public void TakesTheSurfacesBiomeAndCaveBiomesUnderIt()
        {
            // Chunk 0, 0: plains in the west half of its cells and desert in the east, at the surface (y 70); deep
            // dark at the bottom, and plains between. Chunk 1, 0: all plains, so the desert parts the plains in two
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0, 71,
                    Section(-4, Biomes(["deep_dark"])),
                    Section(0, Biomes(["plains"])),
                    Section(4, Biomes(["plains", "desert"], (x, y, z) => x < 2 ? 0 : 1), "grass_block")))
                .Chunk(1, 0, Chunk(1, 0, 71, Section(0, Biomes(["plains"])), Section(4, Biomes(["plains"]), "grass_block")))
                .Save("Biomes");

            DimensionBiomes read = Read(world);

            Assert.Equal(["minecraft:deep_dark", "minecraft:desert", "minecraft:plains"], read.Biomes);
            Assert.Equal(["Deep Dark 16 at 6,6", "Plains 16 at 22,6", "Plains 8 at 2,6", "Desert 8 at 10,6"], read.Patches.Select(Describe));

            BiomePatch desert = read.Patches.Single(p => p.Biome == "minecraft:desert");
            Assert.Equal((8, 0, 15, 15), (desert.MinX, desert.MinZ, desert.MaxX, desert.MaxZ));
            Assert.Equal((8, 16), (desert.Width, desert.Depth));
            Assert.Equal([(0, 2, 3), (1, 2, 3), (2, 2, 3), (3, 2, 3)], desert.Runs);

            // Nearest from the west: its west edge, level with the point; from within, where it is
            Assert.Equal((8, 5, 4.5), desert.NearestTo(3.5, 5.5));
            Assert.Equal((12, 3, 0.0), desert.NearestTo(12.5, 3.5));

            // Nearest the point first, and the bigger first when as near
            var near = DimensionBiomes.ByDistance(read.Patches, 30, 6);
            Assert.Equal(["Plains 16 at 22,6", "Deep Dark 16 at 6,6", "Desert 8 at 10,6", "Plains 8 at 2,6"], near.Select(n => Describe(n.Patch)));
        }

        [Fact]
        public void JoinsPatchesAcrossRegionsAndLeavesOutSmallBits()
        {
            // Chunks 31 and 32 are in different regions. Savanna: a patch of 2 cells (left out) and one of 4; swamp:
            // a single cell, kept as the only one there is
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(31, 0, Chunk(31, 0, 71, Section(4, Biomes(["plains"]))))
                .Chunk(32, 0, Chunk(32, 0, 71, Section(4, Biomes(["plains", "savanna", "swamp"], (x, y, z) =>
                    (x, z) is (0, 3) or (1, 3) ? 1 : x >= 2 && z <= 1 ? 1 : (x, z) == (0, 0) ? 2 : 0))))
                .Save("Across");

            DimensionBiomes read = Read(world);

            BiomePatch plains = read.Patches.Single(p => p.Biome == "minecraft:plains");
            Assert.Equal(16 + 16 - 7, plains.Cells);
            Assert.Equal((496, 527), (plains.MinX, plains.MaxX));
            Assert.Equal(["Savanna 4 at 522,2"], read.Patches.Where(p => p.Biome == "minecraft:savanna").Select(Describe));
            Assert.Equal(["Swamp 1 at 514,2"], read.Patches.Where(p => p.Biome == "minecraft:swamp").Select(Describe));
        }

        [Fact]
        public void ReadsEveryHeightInTheNetherAndSkipsEmptyColumns()
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0, 128, Section(0, Biomes(["crimson_forest"])), Section(4, Biomes(["warped_forest"]))), dimension: "the_nether")
                // Nothing built in it: the first air is the floor
                .Chunk(0, 0, Chunk(0, 0, -64, Section(4, Biomes(["plains"]))))
                .Save("Nether");

            Assert.Equal(["minecraft:crimson_forest", "minecraft:warped_forest"], Read(world, Dimension.Nether).Biomes);
            Assert.Empty(Read(world).Patches);
        }

        // What the Windows smoke test expects of the world it opens
        [Fact]
        public void TheSmokeWorldHasPlainsAndASea()
        {
            using JavaTestWorld world = SmokeWorld.Write();
            DimensionBiomes read = Read(world);
            Assert.Equal(["minecraft:ocean", "minecraft:plains"], read.Biomes);
            Assert.Single(read.Patches, p => p.Biome == "minecraft:ocean");
        }

        [Fact]
        public void SaysWhetherAWordNamesABiome()
        {
            Assert.True(BiomeText.Says("minecraft:cherry_grove", ["cherry"]));
            Assert.True(BiomeText.Says("minecraft:cherry_grove", ["GROVE", "cher"]));
            Assert.True(BiomeText.Says("minecraft:cherry_grove", ["cherry_grove"]));
            Assert.False(BiomeText.Says("minecraft:cherry_grove", ["cherry", "forest"]));
            Assert.True(BiomeText.Says("minecraft:deep_dark", []));
        }

        private class View(double left, double top, double zoom) : IMarkupView
        {
            public string Dimension => "minecraft:overworld";
            public (double X, double Y) ToScreen(double x, double z) => ((x - left) * zoom, (z - top) * zoom);
            public (double X, double Z) ToWorld(double x, double y) => (left + x / zoom, top + y / zoom);
        }

        [Fact]
        public void ShadesRowsZoomedInAndSquaresZoomedOut()
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0, 71, Section(4, Biomes(["plains", "desert"], (x, y, z) => x < 2 ? 0 : 1))))
                .Save("Frame");
            DimensionBiomes read = Read(world);
            BiomePatch desert = read.Patches.Single(p => p.Biome == "minecraft:desert");
            BiomePatch plains = read.Patches.Single(p => p.Biome == "minecraft:plains");

            // Two pixels a block: a rectangle a row of cells, the picked one marked
            List<BiomeMark> near = BiomeFrame.Build([plains, desert], desert, new View(0, 0, 2), 800, 600);
            Assert.Equal(8, near.Count);
            Assert.Equal(new BiomeMark(0, 0, 16, 8, false), near[0]);
            Assert.Equal(new BiomeMark(16, 0, 16, 8, true), near.First(m => m.Picked));

            // A quarter pixel a block: cells come together, 4 to a side, so the two patches make one 16 block square,
            // drawn once
            List<BiomeMark> far = BiomeFrame.Build([plains, desert], null, new View(0, 0, 0.25), 800, 600);
            Assert.Equal([new BiomeMark(0, 0, 4, 4, false)], far);

            // Out of view: nothing
            Assert.Empty(BiomeFrame.Build([plains, desert], null, new View(1000, 1000, 1), 800, 600));
        }
    }
}
