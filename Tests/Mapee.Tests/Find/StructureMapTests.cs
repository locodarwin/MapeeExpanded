using Mapee.Tests.Fixtures;
using Mapper;
using NbtEditor;
using System.Text;
using WorldEditor;
using static Mapee.Tests.Fixtures.BedrockRecords;

namespace Mapee.Tests.Find
{
    // Structures: their names, Java's saved starts old and new, Bedrock's spawn areas, villages and telltale blocks,
    // and where they go on the screen
    public class StructureMapTests
    {
        private sealed class BlockAPixel(double left, double top) : IMarkupView
        {
            public string Dimension => WorldEditor.Dimension.Overworld.Namespace;
            public (double X, double Y) ToScreen(double x, double z) => (x - left, z - top);
            public (double X, double Z) ToWorld(double x, double y) => (left + x, top + y);
        }

        [Theory]
        [InlineData("minecraft:village_plains", "village", "Plains village")]
        [InlineData("minecraft:village_snowy", "village", "Snowy village")]
        [InlineData("minecraft:stronghold", "stronghold", "Stronghold")]
        [InlineData("Desert_Pyramid", "desert_pyramid", "Desert temple")]
        [InlineData("EndCity", "end_city", "End city")]
        [InlineData("Fortress", "fortress", "Nether fortress")]
        [InlineData("minecraft:ruined_portal_mountain", "ruined_portal", "Ruined portal")]
        [InlineData("minecraft:shipwreck_beached", "shipwreck", "Shipwreck")]
        [InlineData("minecraft:ocean_ruin_warm", "ocean_ruin", "Ocean ruin")]
        [InlineData("towers:big_tower", "towers:big_tower", "Big tower")]
        public void NamesTheGamesStructures(string id, string kind, string name)
        {
            var (found, title) = Assert.NotNull(StructureKinds.Of(id));
            Assert.Equal((kind, name), (found.Id, title));
        }

        [Theory]
        [InlineData("minecraft:mineshaft")]
        [InlineData("Mineshaft")]
        [InlineData("minecraft:nether_fossil")]
        [InlineData("INVALID")]
        public void LeavesOutWhatNobodyLooksFor(string id) => Assert.Null(StructureKinds.Of(id));

        [Fact]
        public void ReadsJavaStructureStarts()
        {
            using JavaTestWorld world = SmokeWorld.Write();
            List<Structure> found = StructureMap.Read(new SceneInfo(WorldLoader.ReadLevel(world.LevelDat), Dimension.Overworld));

            Structure village = Assert.Single(found);
            Assert.Equal(("Plains village", StructureSource.Saved), (village.Name, village.Source));
            Assert.Equal((24, 63, 30, 55, 70, 48), (village.MinX, village.MinY, village.MinZ, village.MaxX, village.MaxY, village.MaxZ));
            Assert.Equal(SmokeWorld.VillageMiddle, (village.X, village.Y, village.Z));
        }

        [Fact]
        public void ReadsAgainOnlyTheRegionsGiven()
        {
            using JavaTestWorld world = SmokeWorld.Write();
            SceneInfo scene = new(WorldLoader.ReadLevel(world.LevelDat), Dimension.Overworld);
            DimensionStructures read = StructureMap.Update(null, scene, null);

            // Nothing given, nothing read again; the village's region read again finds it again; one with nothing
            // saved there is dropped
            Assert.Same(read.All[0], StructureMap.Update(read, scene, []).All[0]);
            Structure again = Assert.Single(StructureMap.Update(read, scene, [new Coords(0, 0)]).All);
            Assert.NotSame(read.All[0], again);
            Assert.Equal(read.All[0], again);
            Assert.Single(StructureMap.Update(read, scene, [new Coords(7, 7)]).All);
        }

        [Fact]
        public void ReadsStartsSavedBefore118()
        {
            // Level.Structures.Starts, with old names; a start the game gave up on is INVALID
            byte[] nbt = Nbt.Compound(new()
            {
                ["Level"] = new Dictionary<string, object>
                {
                    ["Structures"] = new Dictionary<string, object>
                    {
                        ["Starts"] = new Dictionary<string, object>
                        {
                            ["Stronghold"] = new Dictionary<string, object>
                            {
                                ["id"] = "Stronghold",
                                ["Children"] = new List<object> { new Dictionary<string, object> { ["BB"] = new[] { -150, 20, -870, -130, 40, -850 } } }
                            },
                            ["Village"] = new Dictionary<string, object> { ["id"] = "INVALID" }
                        }
                    }
                }
            }, littleEndian: false);
            CompoundTag chunk = (CompoundTag)Tag.FromNbtReader(new UnsafeNbtReader(nbt, 0));

            Structure stronghold = Assert.Single(StructureMap.Starts(Dimension.Overworld, chunk));
            Assert.Equal(("Stronghold", -140, 30, -860), (stronghold.Name, stronghold.X, stronghold.Y, stronghold.Z));
        }

        private static byte[] SpawnAreas(params (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ, byte Kind)[] boxes)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            writer.Write(boxes.Length);
            foreach (var box in boxes)
            {
                foreach (int value in new[] { box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ }) writer.Write(value);
                writer.Write(box.Kind);
            }
            return stream.ToArray();
        }

        private static byte[] VillageInfo(int x0, int y0, int z0, int x1, int y1, int z1) => Nbt.Compound(new()
        {
            ["X0"] = x0, ["Y0"] = y0, ["Z0"] = z0, ["X1"] = x1, ["Y1"] = y1, ["Z1"] = z1, ["Initialized"] = (byte)1
        });

        [Fact]
        public void ReadsBedrockSpawnAreasVillagesAndTelltaleBlocks()
        {
            Dictionary<string, object>[] palette = [Nbt.Block("minecraft:air"), Nbt.Block("minecraft:end_portal_frame")];
            using BedrockTestWorld world = new BedrockTestWorld()
                .Chunk(0, 0)
                .Chunk(1, 0)
                .Chunk(0, 0, dimension: 1)
                // A witch hut whose spawn area two chunks both keep, and an outpost
                .Put(BedrockTestWorld.Key(0, 0, 0, (BedrockChunkTag)57), SpawnAreas((2, 64, 2, 8, 68, 8, 2), (100, 70, 0, 110, 80, 10, 5)))
                .Put(BedrockTestWorld.Key(1, 0, 0, (BedrockChunkTag)57), SpawnAreas((2, 64, 2, 8, 68, 8, 2)))
                // Two pieces of one fortress, 100 blocks apart; a kind Mapee doesn't know is left out
                .Put(BedrockTestWorld.Key(0, 0, 1, (BedrockChunkTag)57), SpawnAreas((0, 60, 0, 4, 70, 4, 1), (104, 60, 0, 110, 70, 4, 1), (0, 0, 0, 1, 1, 1, 9)))
                // A village kept as two that overlap, an older one without a dimension, and one in the Nether
                .Put(Encoding.ASCII.GetBytes("VILLAGE_Overworld_0e4aa64d-53a8-4f6f-b4ca-26be9a8f7f7e_INFO"), VillageInfo(200, 60, 200, 260, 80, 260))
                .Put(Encoding.ASCII.GetBytes("VILLAGE_Overworld_11111111-53a8-4f6f-b4ca-26be9a8f7f7e_INFO"), VillageInfo(250, 60, 250, 280, 80, 280))
                .Put(Encoding.ASCII.GetBytes("VILLAGE_22222222-53a8-4f6f-b4ca-26be9a8f7f7e_INFO"), VillageInfo(-300, 60, -300, -260, 70, -260))
                .Put(Encoding.ASCII.GetBytes("VILLAGE_Nether_33333333-53a8-4f6f-b4ca-26be9a8f7f7e_INFO"), VillageInfo(0, 40, 0, 10, 50, 10))
                .Put(Encoding.ASCII.GetBytes("VILLAGE_Overworld_0e4aa64d-53a8-4f6f-b4ca-26be9a8f7f7e_DWELLERS"), [0])
                // A stronghold's portal frame, deep down
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 2), PaletteSubChunk(9, 2, palette, (x, y, z) =>
                    y == 0 && ((x is 4 or 8 && z is >= 5 and <= 7) || (z is 4 or 8 && x is >= 5 and <= 7)) ? 1 : 0))
                .Save("Bedrock structure test");
            Level level = WorldLoader.ReadLevel(world.LevelDat);

            List<Structure> overworld = StructureMap.Read(new SceneInfo(level, Dimension.Overworld));
            Assert.Equal(["Stronghold", "Village", "Village", "Pillager outpost", "Witch hut"], overworld.Select(s => s.Name));

            Structure stronghold = overworld[0];
            Assert.Equal((StructureSource.Blocks, 4, 32, 4, 8, 32, 8), (stronghold.Source, stronghold.MinX, stronghold.MinY, stronghold.MinZ, stronghold.MaxX, stronghold.MaxY, stronghold.MaxZ));
            Assert.Equal((-300, 200, 280, 280), (overworld[1].MinX, overworld[2].MinX, overworld[2].MaxX, overworld[2].MaxZ));
            Assert.Equal((2, 8), (overworld[4].MinX, overworld[4].MaxX));

            Structure fortress = Assert.Single(StructureMap.Read(new SceneInfo(level, Dimension.Nether)), s => s.Kind.Id == "fortress");
            Assert.Equal((0, 110), (fortress.MinX, fortress.MaxX));
        }

        [Fact]
        public void KeepsStructuresOfOneKindApartBeyondTheGap()
        {
            StructureKind hut = StructureKinds.Get("swamp_hut");
            Structure Hut(int x) => new(Dimension.Overworld, hut, hut.Name, x, 64, 0, x + 6, 70, 6, StructureSource.Saved);

            Assert.Equal(2, StructureMap.Merge([Hut(0), Hut(6 + StructureMap.Gap + 1)]).Count);
            Assert.Single(StructureMap.Merge([Hut(0), Hut(6 + StructureMap.Gap)]));
        }

        [Fact]
        public void LabelsWhatMattersMostAndTurnsAtTheEdge()
        {
            StructureKind stronghold = StructureKinds.Get("stronghold"), ruin = StructureKinds.Get("ocean_ruin");
            List<Structure> structures =
            [
                new(Dimension.Overworld, stronghold, stronghold.Name, 100, 30, 100, 140, 40, 140, StructureSource.Saved),
                new(Dimension.Overworld, ruin, ruin.Name, 122, 60, 120, 124, 62, 122, StructureSource.Saved),
                new(Dimension.Overworld, ruin, ruin.Name, 390, 60, 200, 392, 62, 202, StructureSource.Saved),
                new(Dimension.Overworld, ruin, ruin.Name, 2000, 60, 2000, 2002, 62, 2002, StructureSource.Saved),
            ];
            StructureFrame frame = StructureFrame.Build(structures, new BlockAPixel(0, 0), 400, 400);

            // The far one is off the screen; the ruin by the stronghold has no room for its name
            Assert.Equal(3, frame.Shapes.Count);
            Assert.Equal([true, false, true], frame.Shapes.Select(s => s.Labelled));
            Assert.NotNull(frame.Shapes[0].Box);
            Assert.Null(frame.Shapes[1].Box);
            Assert.Equal([false, false, true], frame.Shapes.Select(s => s.LabelOnLeft));
        }
    }
}
