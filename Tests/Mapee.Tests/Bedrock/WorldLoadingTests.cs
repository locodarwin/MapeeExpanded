using Mapee.Tests.Fixtures;
using WorldEditor;
using static Mapee.Tests.Fixtures.BedrockRecords;
using static Mapee.Tests.Fixtures.ChunkInspection;

namespace Mapee.Tests.Bedrock
{
    public class WorldLoadingTests
    {
        // A world played since before 1.0 and opened in every era since holds chunks in every format
        private static BedrockTestWorld MixedWorld(byte? tableCompression = null)
        {
            Dictionary<string, object>[] modern = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:deepslate", new() { ["pillar_axis"] = "y" }), Nbt.Block("minecraft:cherry_planks")];
            Dictionary<string, object>[] dataValues = [Nbt.LegacyBlock("minecraft:air", 0), Nbt.LegacyBlock("minecraft:sandstone", 0)];

            return new BedrockTestWorld()
                // Before 1.0, indexed by the old version key
                .Chunk(0, 0, legacyVersionKey: true, version: 2)
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.LegacyTerrain), LegacyTerrain((x, y, z) => y <= 62 ? ((byte)2, (byte)0) : ((byte)0, (byte)0), (x, z) => 4))
                // 1.2.13 to 1.12
                .Chunk(1, 0, version: 10)
                .Put(BedrockTestWorld.Key(1, 0, 0, BedrockChunkTag.SubChunkPrefix, 3), PaletteSubChunk(8, 3, dataValues, (x, y, z) => y < 8 ? 1 : 0))
                .Put(BedrockTestWorld.Key(1, 0, 0, BedrockChunkTag.Data2D), Data2D((x, z) => 56, (x, z) => 2))
                // 1.18 and later
                .Chunk(2, 0)
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.SubChunkPrefix, -4), PaletteSubChunk(9, -4, modern, (x, y, z) => 1))
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.SubChunkPrefix, 4), PaletteSubChunk(9, 4, modern, (x, y, z) => y == 0 ? 2 : 0))
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.Data3D), Data3D((x, z) => 64 + 65, 1))
                // A Nether chunk with numeric IDs
                .Chunk(0, 0, dimension: 1)
                .Put(BedrockTestWorld.Key(0, 0, 1, BedrockChunkTag.SubChunkPrefix, 0), NumericSubChunk(2, (x, y, z) => y < 10 ? ((byte)87, (byte)0) : ((byte)0, (byte)0)))
                .Save("Mixed formats", tableCompression);
        }

        // Both as just written (in the log) and as compacted into a table with raw deflate blocks
        [Theory]
        [InlineData(null)]
        [InlineData((byte)4)]
        public void LoadsEveryChunkFormat(byte? tableCompression)
        {
            using BedrockTestWorld world = MixedWorld(tableCompression);
            Level level = WorldLoader.ReadLevel(world.LevelDat);
            Assert.Equal(Edition.Bedrock, level.Edition);
            Assert.Equal("Mixed formats", level.WorldName);

            IReadOnlyDictionary<Coords, ConvertedApiChunk> chunks = WorldLoader.LoadChunks(level, Dimension.Overworld);
            Assert.Equal(3, chunks.Count);

            BlockStateChunk ancient = (BlockStateChunk)chunks[new Coords(0, 0)].BlockState!;
            Assert.Equal("minecraft:grass_block", NameAt(ancient, 8, 62, 8));
            Assert.Equal(63, HeightAt((HeightmapCollection)chunks[new Coords(0, 0)].Heightmap!, 8, 8));

            BlockStateChunk old = (BlockStateChunk)chunks[new Coords(1, 0)].BlockState!;
            Assert.Equal("minecraft:sandstone", NameAt(old, 4, 50, 4));
            Assert.Equal("minecraft:desert", BiomeAt((BiomeChunk)chunks[new Coords(1, 0)].Biome!, 4, 50, 4));

            BlockStateChunk modern = (BlockStateChunk)chunks[new Coords(2, 0)].BlockState!;
            Assert.Equal("minecraft:deepslate", NameAt(modern, 0, -60, 0));
            Assert.Equal("minecraft:cherry_planks", NameAt(modern, 0, 64, 0));
            Assert.Equal(65, HeightAt((HeightmapCollection)chunks[new Coords(2, 0)].Heightmap!, 0, 0));
        }

        [Fact]
        public void LoadsOldNether()
        {
            using BedrockTestWorld world = MixedWorld();
            IReadOnlyDictionary<Coords, ConvertedApiChunk> chunks = WorldLoader.LoadChunks(WorldLoader.ReadLevel(world.LevelDat), Dimension.Nether);

            BlockStateChunk nether = (BlockStateChunk)Assert.Single(chunks).Value.BlockState!;
            Assert.Equal("minecraft:netherrack", NameAt(nether, 3, 9, 3));
        }

        // Records over 32 KiB are split across log blocks; LegacyTerrain alone is 83 KB
        [Fact]
        public void ReadsRecordsSplitAcrossLogBlocks()
        {
            using BedrockTestWorld world = MixedWorld();
            using LevelDbReader db = new(Path.Combine(world.Directory, "db"));

            Assert.True(db.TryGet(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.LegacyTerrain), out byte[]? terrain));
            Assert.Equal(83200, terrain!.Length);
            Assert.Equal(4, db.GetByPrefix(BedrockKey.ChunkPrefix(2, 0, 0)).Count);
        }
    }
}
