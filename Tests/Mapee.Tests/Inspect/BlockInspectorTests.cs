using Mapee.Tests.Fixtures;
using Mapper;
using WorldEditor;
using static Mapee.Tests.Fixtures.BedrockRecords;

namespace Mapee.Tests.Inspect
{
    // What the info bar reports under the mouse, for the surface, a slice and caves
    public class BlockInspectorTests
    {
        // Every column alike, bottom up: stone 0-3, a cave 4-7, stone 8-15, sand 16-17, water 18-22, then air
        private static Dictionary<string, object> Chunk(int x, int z)
        {
            static Dictionary<string, object> Block(string name) => new() { ["Name"] = name };
            List<object> palette = [Block("minecraft:air"), Block("minecraft:stone"), Block("minecraft:cave_air"), Block("minecraft:sand"), Block("minecraft:water")];

            return new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = 0,
                ["Status"] = "minecraft:full",
                ["sections"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["Y"] = (byte)0,
                        ["block_states"] = new Dictionary<string, object>
                        {
                            ["palette"] = palette,
                            ["data"] = JavaTestWorld.PackBlockStates(palette.Count, (bx, by, bz) => by is >= 4 and <= 7 ? 2 : 1)
                        },
                        ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:dripstone_caves" } }
                    },
                    new Dictionary<string, object>
                    {
                        ["Y"] = (byte)1,
                        ["block_states"] = new Dictionary<string, object>
                        {
                            ["palette"] = palette,
                            ["data"] = JavaTestWorld.PackBlockStates(palette.Count, (bx, by, bz) => by < 2 ? 3 : by < 7 ? 4 : 0)
                        },
                        ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:ocean" } }
                    }
                }
            };
        }

        private static (BlockInspector Inspector, IRegionStore Store, JavaTestWorld World) JavaWorld()
        {
            JavaTestWorld world = new JavaTestWorld().Chunk(0, 0, Chunk(0, 0)).Chunk(-1, -1, Chunk(-1, -1)).Save();
            Level level = WorldLoader.ReadLevel(world.LevelDat);
            return (new BlockInspector(), new SceneInfo(level, Dimension.Overworld).RegionStore, world);
        }

        [Fact]
        public void SurfaceWaterReportsItsDepthAndFloor()
        {
            var (inspector, store, world) = JavaWorld();
            using (world)
            {
                InspectedColumn column = inspector.InspectNow(new InspectRequest(store, 5, 9, null, false))!;

                Assert.Equal("minecraft:water", column.Block.Name);
                Assert.Equal(22, column.Y);
                Assert.Equal(5, column.Depth);
                Assert.Equal("minecraft:sand", column.Floor?.Name);
                Assert.Equal(17, column.FloorY);
                Assert.Equal("minecraft:ocean", column.Biome);
            }
        }

        [Fact]
        public void SliceReportsTheBlockAtTheCut()
        {
            var (inspector, store, world) = JavaWorld();
            using (world)
            {
                InspectedColumn column = inspector.InspectNow(new InspectRequest(store, 5, 9, 12, false))!;

                Assert.Equal("minecraft:stone", column.Block.Name);
                Assert.Equal(12, column.Y);
                Assert.Equal(0, column.Depth);
                Assert.Equal("minecraft:dripstone_caves", column.Biome);
            }
        }

        [Fact]
        public void CavesReportTheFloorBelowTheFirstAir()
        {
            var (inspector, store, world) = JavaWorld();
            using (world)
            {
                InspectedColumn column = inspector.InspectNow(new InspectRequest(store, 5, 9, 12, true))!;

                Assert.Equal("minecraft:stone", column.Block.Name);
                Assert.Equal(3, column.Y);
            }
        }

        [Fact]
        public void NegativeCoordinatesFindTheirChunk()
        {
            var (inspector, store, world) = JavaWorld();
            using (world)
            {
                Assert.Equal(22, inspector.InspectNow(new InspectRequest(store, -1, -1, null, false))?.Y);
                Assert.Equal(22, inspector.InspectNow(new InspectRequest(store, -16, -16, null, false))?.Y);
                // Chunks (-1, 0) and (1, 1) were never generated
                Assert.Null(inspector.InspectNow(new InspectRequest(store, -1, 0, null, false)));
                Assert.Null(inspector.InspectNow(new InspectRequest(store, 16, 16, null, false)));
            }
        }

        [Fact]
        public void ReadsBedrockChunks()
        {
            Dictionary<string, object>[] palette = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:deepslate", new() { ["pillar_axis"] = "y" }), Nbt.Block("minecraft:cherry_planks")];
            using BedrockTestWorld world = new BedrockTestWorld()
                .Chunk(2, 0)
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.SubChunkPrefix, -4), PaletteSubChunk(9, -4, palette, (x, y, z) => 1))
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.SubChunkPrefix, 4), PaletteSubChunk(9, 4, palette, (x, y, z) => y == 0 ? 2 : 0))
                .Put(BedrockTestWorld.Key(2, 0, 0, BedrockChunkTag.Data3D), Data3D((x, z) => 64 + 65, 1))
                .Save("Inspected");

            Level level = WorldLoader.ReadLevel(world.LevelDat);
            IRegionStore store = new SceneInfo(level, Dimension.Overworld).RegionStore;
            ((IBackgroundRegionStore)store).Ready.Wait();

            InspectedColumn column = new BlockInspector().InspectNow(new InspectRequest(store, 40, 3, null, false))!;
            Assert.Equal("minecraft:cherry_planks", column.Block.Name);
            Assert.Equal(64, column.Y);

            // Below the planks the space is open down to the deepslate
            column = new BlockInspector().InspectNow(new InspectRequest(store, 40, 3, 63, true))!;
            Assert.Equal("minecraft:deepslate", column.Block.Name);
            Assert.Equal(-49, column.Y);
        }

        [Theory]
        [InlineData("minecraft:grass_block", "Grass Block")]
        [InlineData("minecraft:tnt", "Tnt")]
        [InlineData("create:brass_casing", "create:Brass Casing")]
        [InlineData("stone", "Stone")]
        public void NamesReadAsWords(string name, string expected)
        {
            Assert.Equal(expected, ColumnInspector.DisplayName(name));
        }
    }
}
