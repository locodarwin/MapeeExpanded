using Mapee.Tests.Fixtures;
using WorldEditor;
using static Mapee.Tests.Fixtures.ChunkInspection;

namespace Mapee.Tests.Java
{
    // Java worlds are upstream Mapee's own ground; these guard the parts the fork changed underneath them,
    // such as the zlib library swap that made the libraries run on Linux
    public class JavaWorldTests
    {
        private static Dictionary<string, object> Chunk(int x, int z)
        {
            List<object> palette =
            [
                new Dictionary<string, object> { ["Name"] = "minecraft:air" },
                new Dictionary<string, object> { ["Name"] = "minecraft:grass_block", ["Properties"] = new Dictionary<string, object> { ["snowy"] = "false" } },
                new Dictionary<string, object> { ["Name"] = "minecraft:oak_log", ["Properties"] = new Dictionary<string, object> { ["axis"] = "y" } }
            ];

            return new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["Y"] = unchecked((byte)-4),
                        ["block_states"] = new Dictionary<string, object> { ["palette"] = new List<object> { new Dictionary<string, object> { ["Name"] = "minecraft:bedrock" } } },
                        ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:plains" } }
                    },
                    new Dictionary<string, object>
                    {
                        ["Y"] = (byte)4,
                        ["block_states"] = new Dictionary<string, object>
                        {
                            ["palette"] = palette,
                            ["data"] = JavaTestWorld.PackBlockStates(palette.Count, (bx, by, bz) => by == 0 ? 1 : bx == 7 && bz == 7 && by < 5 ? 2 : 0)
                        },
                        ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:desert" } }
                    }
                }
            };
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        [InlineData(3)]
        public void LoadsChunksWithEachCompression(byte compression)
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0), compression)
                .Chunk(1, 0, Chunk(1, 0), compression)
                .Chunk(-1, -1, Chunk(-1, -1), compression)
                .Save("Java world");

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            Assert.Equal(Edition.Java, level.Edition);
            Assert.Equal("Java world", level.WorldName);
            Assert.Equal("26.2", level.Version.VersionName);

            IReadOnlyDictionary<Coords, ConvertedApiChunk> chunks = WorldLoader.LoadChunks(level, Dimension.Overworld);
            Assert.Equal(3, chunks.Count);

            BlockStateChunk blocks = (BlockStateChunk)chunks[new Coords(-1, -1)].BlockState!;
            Assert.Equal("minecraft:bedrock", NameAt(blocks, 3, -64, 3));
            Assert.Equal("minecraft:grass_block", NameAt(blocks, 3, 64, 3));
            Assert.Equal("minecraft:oak_log", NameAt(blocks, 7, 68, 7));
            Assert.Equal("minecraft:air", NameAt(blocks, 7, 69, 7));
        }
    }
}
