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
    
        // 26.1 moved every dimension under dimensions/minecraft/; the Nether's folder is the_nether
        [Fact]
        public void LoadsEachDimensionFromItsOwnFolder()
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0))
                .Chunk(1, 0, Chunk(1, 0), dimension: "the_nether")
                .Chunk(2, 0, Chunk(2, 0), dimension: "the_end")
                .Save();

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            Assert.Equal([new Coords(0, 0)], WorldLoader.LoadChunks(level, Dimension.Overworld).Keys);
            Assert.Equal([new Coords(1, 0)], WorldLoader.LoadChunks(level, Dimension.Nether).Keys);
            Assert.Equal([new Coords(2, 0)], WorldLoader.LoadChunks(level, Dimension.TheEnd).Keys);
        }

        // 26.x keeps the single player in players/data/<uuid>.dat rather than in level.dat
        [Theory]
        [InlineData("minecraft:overworld", "Overworld")]
        [InlineData("minecraft:the_nether", "Nether")]
        [InlineData("minecraft:the_end", "The End")]
        public void ReadsThePlayerFromItsOwnFile(string dimension, string expected)
        {
            using JavaTestWorld world = new JavaTestWorld()
                .Chunk(0, 0, Chunk(0, 0))
                .Player(new()
                {
                    ["Dimension"] = dimension,
                    ["Pos"] = new List<object> { 120.5, 70.0, -33.25 },
                    ["playerGameType"] = 0,
                    ["respawn"] = new Dictionary<string, object> { ["pos"] = new[] { 10, 64, -20 }, ["dimension"] = "minecraft:overworld" }
                })
                .Save();

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            Assert.Equal(expected, level.Player.Dimension.Name);
            Assert.Equal(new System.Numerics.Vector3(120.5f, 70f, -33.25f), level.Player.Position);
            Assert.Equal(new System.Numerics.Vector3(10, 64, -20), level.Player.Spawn);
            Assert.Equal(GameType.Survival, level.Player.GameType);
        }
    }
}
