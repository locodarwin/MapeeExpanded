using Mapee.Tests.Fixtures;
using WorldEditor;
using static Mapee.Tests.Fixtures.BedrockRecords;
using static Mapee.Tests.Fixtures.ChunkInspection;

namespace Mapee.Tests.Bedrock
{
    // Chunks keep the format they were last saved in, so one world can hold all of these
    public class OldFormatTests
    {
        private readonly BedrockChunkReader _reader = new() { ComputeSkyLight = false, ComputeBlockLight = false };

        private ApiChunk Read(params (byte[] Key, byte[] Value)[] records)
        {
            ApiChunk? chunk = _reader.Read(0, 0, 0, Records(records));
            Assert.NotNull(chunk);
            return chunk;
        }

        [Fact]
        public void ModernPaletteSubChunk()
        {
            Dictionary<string, object>[] palette = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:stone"), Nbt.Block("minecraft:white_wool")];
            byte[] subChunk = PaletteSubChunk(9, 4, palette, (x, y, z) => y < 8 ? 1 : x == 3 && y == 8 && z == 5 ? 2 : 0);

            BlockStateChunk blocks = (BlockStateChunk)Read((BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 4), subChunk)).BlockState!;

            Assert.Equal("minecraft:stone", NameAt(blocks, 0, 64, 0));
            Assert.Equal("minecraft:white_wool", NameAt(blocks, 3, 72, 5));
            Assert.Equal("minecraft:air", NameAt(blocks, 4, 72, 5));
        }

        // 1.13 to 1.18: states with the names and properties of the time
        [Fact]
        public void OldBlockStates()
        {
            Dictionary<string, object>[] palette =
            [
                Nbt.Block("minecraft:air"),
                Nbt.Block("minecraft:wool", new() { ["color"] = "lime" }),
                Nbt.Block("minecraft:log", new() { ["old_log_type"] = "spruce", ["pillar_axis"] = "y" }),
                Nbt.Block("minecraft:stone", new() { ["stone_type"] = "granite" })
            ];
            byte[] subChunk = PaletteSubChunk(8, 0, palette, (x, y, z) => x < 4 ? x : 0);

            BlockStateChunk blocks = (BlockStateChunk)Read((BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 0), subChunk)).BlockState!;

            Assert.Equal("minecraft:lime_wool", NameAt(blocks, 1, 0, 0));
            Block log = BlockAt(blocks, 2, 0, 0)!.Value;
            Assert.Equal("minecraft:spruce_log", log.Name);
            Assert.Contains(log.Properties, p => p.Name == "axis" && p.Value == "y");
            Assert.Equal("minecraft:granite", NameAt(blocks, 3, 0, 0));
        }

        // 1.2.13 to 1.12: palettes of names and data values
        [Fact]
        public void DataValuePalette()
        {
            Dictionary<string, object>[] palette =
            [
                Nbt.LegacyBlock("minecraft:air", 0),
                Nbt.LegacyBlock("minecraft:wool", 14),
                Nbt.LegacyBlock("minecraft:planks", 5),
                Nbt.LegacyBlock("minecraft:stone", 3)
            ];
            byte[] subChunk = PaletteSubChunk(8, 2, palette, (x, y, z) => z < 4 ? z : 0);

            BlockStateChunk blocks = (BlockStateChunk)Read((BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 2), subChunk)).BlockState!;

            Assert.Equal("minecraft:red_wool", NameAt(blocks, 0, 32, 1));
            Assert.Equal("minecraft:dark_oak_planks", NameAt(blocks, 0, 32, 2));
            Assert.Equal("minecraft:diorite", NameAt(blocks, 0, 32, 3));
        }

        // Before 1.2.13: numeric IDs, including ones Bedrock numbered differently from Java
        [Fact]
        public void NumericIds()
        {
            byte[] subChunk = NumericSubChunk(0, (x, y, z) => (x, y, z) switch
            {
                (1, 3, 2) => (35, 5),   // lime wool
                (2, 3, 2) => (198, 0),  // grass path, 208 in Java
                (3, 3, 2) => (158, 2),  // birch slab, 126 in Java
                (4, 3, 2) => (241, 4),  // yellow stained glass, 95 in Java
                (_, < 3, _) => (1, 0),
                _ => (0, 0)
            });

            BlockStateChunk blocks = (BlockStateChunk)Read((BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 1), subChunk)).BlockState!;

            Assert.Equal("minecraft:stone", NameAt(blocks, 9, 16, 9));
            Assert.Equal("minecraft:lime_wool", NameAt(blocks, 1, 19, 2));
            Assert.Equal("minecraft:dirt_path", NameAt(blocks, 2, 19, 2));
            Assert.Equal("minecraft:birch_slab", NameAt(blocks, 3, 19, 2));
            Assert.Equal("minecraft:yellow_stained_glass", NameAt(blocks, 4, 19, 2));
            Assert.Equal("minecraft:air", NameAt(blocks, 1, 20, 2));
        }

        [Fact]
        public void Data2DGivesHeightsAndBiomes()
        {
            Dictionary<string, object>[] palette = [Nbt.LegacyBlock("minecraft:air", 0), Nbt.LegacyBlock("minecraft:sand", 0)];
            byte[] subChunk = PaletteSubChunk(8, 3, palette, (x, y, z) => y < 4 ? 1 : 0);
            byte[] data2D = Data2D((x, z) => 52, (x, z) => x < 8 ? (byte)2 : (byte)1); // desert, plains

            ApiChunk chunk = Read(
                (BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 3), subChunk),
                (BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.Data2D), data2D));

            Assert.Equal(52, HeightAt((HeightmapCollection)chunk.Heightmap!, 7, 7));
            BiomeChunk biomes = (BiomeChunk)chunk.Biome!;
            Assert.Equal("minecraft:desert", BiomeAt(biomes, 2, 50, 9));
            Assert.Equal("minecraft:plains", BiomeAt(biomes, 13, 50, 9));
        }

        // Before 1.0: one record for the whole chunk, 128 blocks high
        [Fact]
        public void WholeChunkBeforeOnePointZero()
        {
            byte[] terrain = LegacyTerrain(
                (x, y, z) => y < 60 ? ((byte)1, (byte)0) : y == 60 ? ((byte)2, (byte)0) : x == 5 && z == 6 && y == 100 ? ((byte)35, (byte)11) : ((byte)0, (byte)0),
                (x, z) => z < 8 ? (byte)4 : (byte)2); // forest, desert

            ApiChunk chunk = Read((BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.LegacyTerrain), terrain));
            BlockStateChunk blocks = (BlockStateChunk)chunk.BlockState!;

            Assert.Equal("minecraft:stone", NameAt(blocks, 0, 0, 0));
            Assert.Equal("minecraft:grass_block", NameAt(blocks, 15, 60, 15));
            Assert.Equal("minecraft:blue_wool", NameAt(blocks, 5, 100, 6));
            Assert.Null(BlockAt(blocks, 0, 80, 0)); // all-air sections are dropped

            HeightmapCollection heights = (HeightmapCollection)chunk.Heightmap!;
            Assert.Equal(61, HeightAt(heights, 0, 0));
            Assert.Equal(101, HeightAt(heights, 5, 6));

            BiomeChunk biomes = (BiomeChunk)chunk.Biome!;
            Assert.Equal("minecraft:forest", BiomeAt(biomes, 0, 60, 0));
            Assert.Equal("minecraft:desert", BiomeAt(biomes, 0, 60, 12));
        }

        [Fact]
        public void ModernData3DStillWins()
        {
            Dictionary<string, object>[] palette = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:stone")];
            byte[] subChunk = PaletteSubChunk(9, 0, palette, (x, y, z) => y < 5 ? 1 : 0);

            ApiChunk chunk = Read(
                (BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 0), subChunk),
                (BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.Data3D), Data3D((x, z) => 64 + 5, 1)));

            Assert.Equal(5, HeightAt((HeightmapCollection)chunk.Heightmap!, 3, 3));
            Assert.Equal("minecraft:plains", BiomeAt((BiomeChunk)chunk.Biome!, 3, 2, 3));
        }
    }
}
