using Mapee.Tests.Fixtures;
using Mapper;
using NbtEditor;
using WorldEditor;

namespace Mapee.Tests.Find
{
    // Signs: what each edition and version saves on them, reading a world's and reading again what was saved, and
    // the signs picked to show: kept with the markup, brought up to date, hovered and labelled
    public class SignMapTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-signs-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        // A block a screen unit, with block 0, 0 at the top left
        private sealed class BlockAPixel : IMarkupView
        {
            public string Dimension => WorldEditor.Dimension.Overworld.Namespace;
            public (double X, double Y) ToScreen(double x, double z) => (x, z);
            public (double X, double Z) ToWorld(double x, double y) => (x, y);
        }

        private static CompoundTag Read(Dictionary<string, object> values, bool littleEndian) =>
            (CompoundTag)Tag.FromNbtReader(new UnsafeNbtReader(Nbt.Compound(values, littleEndian), 0) { IsFormatLittleEndian = littleEndian });

        private static Dictionary<string, object> JavaSide(params string[] lines) => new() { ["messages"] = lines.Cast<object>().ToList(), ["color"] = "black" };

        [Theory]
        [InlineData("{\"text\":\"Home\"}", "Home")]
        [InlineData("\"Home\"", "Home")]
        [InlineData("{\"text\":\"\",\"extra\":[{\"text\":\"Ho\",\"bold\":true},\"me\"]}", "Home")]
        [InlineData("[\"\",{\"text\":\"Home\"}]", "Home")]
        [InlineData("Home", "Home")]
        [InlineData("§cHo§lme", "Home")]
        [InlineData("{\"text\":\"§aHome\"}", "Home")]
        [InlineData("{broken", "{broken")]
        public void ReadsTheWordsWithoutTheirFormatting(string saved, string words) => Assert.Equal(words, SignText.Plain(new StringTag(saved)));

        [Fact]
        public void ReadsJavaSignsOfEveryVersion()
        {
            CompoundTag chunk = Read(new()
            {
                ["xPos"] = 0,
                ["zPos"] = 0,
                ["block_entities"] = new List<object>
                {
                    // 1.20 to 1.21.4: JSON lines on two sides
                    new Dictionary<string, object> { ["id"] = "minecraft:sign", ["x"] = 1, ["y"] = 64, ["z"] = 2,
                        ["front_text"] = JavaSide("{\"text\":\"Diamond\"}", "{\"text\":\"Mine\"}", "\"\"", "\"\""), ["back_text"] = JavaSide("\"Keep out\"", "\"\"", "\"\"", "\"\"") },
                    // From 1.21.5 the lines are NBT text, a string or a compound
                    new Dictionary<string, object> { ["id"] = "minecraft:hanging_sign", ["x"] = 3, ["y"] = 70, ["z"] = 4,
                        ["front_text"] = new Dictionary<string, object> { ["messages"] = new List<object>
                        {
                            new Dictionary<string, object> { ["text"] = "Shop" },
                            new Dictionary<string, object> { ["text"] = "", ["extra"] = new List<object> { new Dictionary<string, object> { ["text"] = "open" } } },
                            new Dictionary<string, object> { ["text"] = "" },
                            new Dictionary<string, object> { ["text"] = "" }
                        } } },
                    new Dictionary<string, object> { ["id"] = "minecraft:chest", ["x"] = 5, ["y"] = 64, ["z"] = 5 }
                }
            }, littleEndian: false);
            List<Sign> signs = [.. SignMap.InChunk(Dimension.Overworld, chunk)];

            Assert.Equal(2, signs.Count);
            Assert.Equal((1, 64, 2, false), (signs[0].X, signs[0].Y, signs[0].Z, signs[0].Hanging));
            Assert.Equal(["Diamond", "Mine"], signs[0].Front);
            Assert.Equal(["Keep out"], signs[0].Back);
            Assert.Equal(("Diamond Mine / Keep out", "Diamond"), (signs[0].Text, signs[0].Title));
            Assert.Equal((true, "Shop open", "Hanging sign"), (signs[1].Hanging, signs[1].Text, signs[1].Kind));

            // Before 1.20: four lines, in Level.TileEntities before 1.18, and Sign as its id before 1.11
            CompoundTag old = Read(new()
            {
                ["Level"] = new Dictionary<string, object>
                {
                    ["xPos"] = 0,
                    ["zPos"] = 0,
                    ["TileEntities"] = new List<object>
                    {
                        new Dictionary<string, object> { ["id"] = "Sign", ["x"] = 7, ["y"] = 65, ["z"] = 8,
                            ["Text1"] = "{\"text\":\"Old\"}", ["Text2"] = "\"\"", ["Text3"] = "\"road\"", ["Text4"] = "\"\"" }
                    }
                }
            }, littleEndian: false);
            Sign oldSign = Assert.Single(SignMap.InChunk(Dimension.Overworld, old));
            Assert.Equal(["Old", "", "road"], oldSign.Front);
            Assert.Equal("Old road", oldSign.Text);
        }

        [Fact]
        public void ReadsBedrockSignsOfEveryVersion()
        {
            byte[] record = [
                // From 1.19.80: a text on each side, its lines split by new lines, with colour codes
                .. Nbt.Compound(new() { ["id"] = "Sign", ["x"] = -10, ["y"] = 64, ["z"] = 20,
                    ["FrontText"] = new Dictionary<string, object> { ["Text"] = "§4Nether\nportal" }, ["BackText"] = new Dictionary<string, object> { ["Text"] = "" } }),
                .. Nbt.Compound(new() { ["id"] = "Chest", ["x"] = -9, ["y"] = 64, ["z"] = 20 }),
                // Before: one text
                .. Nbt.Compound(new() { ["id"] = "Sign", ["x"] = -8, ["y"] = 64, ["z"] = 20, ["Text"] = "Farm\n\n\n" }),
                .. Nbt.Compound(new() { ["id"] = "HangingSign", ["x"] = -7, ["y"] = 66, ["z"] = 20,
                    ["FrontText"] = new Dictionary<string, object> { ["Text"] = "" }, ["BackText"] = new Dictionary<string, object> { ["Text"] = "" } }),
            ];
            List<Sign> signs = SignMap.InRecord(Dimension.Overworld, record);

            Assert.Equal(3, signs.Count);
            Assert.Equal(["Nether", "portal"], signs[0].Front);
            Assert.Empty(signs[0].Back);
            Assert.Equal(["Farm"], signs[1].Front);
            Assert.True(signs[2].Hanging);
            Assert.True(signs[2].IsBlank);

            // A record cut short keeps the signs before the cut
            Assert.Equal(2, SignMap.InRecord(Dimension.Overworld, record[..^20]).Count);
        }

        [Fact]
        public void ReadsAJavaWorldAndAgainOnlyTheRegionsGiven()
        {
            using JavaTestWorld world = SmokeWorld.Write();
            SceneInfo scene = new(WorldLoader.ReadLevel(world.LevelDat), Dimension.Overworld);
            DimensionSigns read = SignMap.Update(null, scene, null);

            Sign sign = Assert.Single(read.All);
            Assert.Equal(SmokeWorld.SignAt, (sign.X, sign.Y, sign.Z));
            Assert.Equal(SmokeWorld.SignFront, sign.Front);
            Assert.Same(sign, read.At(sign.X, sign.Y, sign.Z));

            // Nothing given, nothing read again; its region read again finds it again
            Assert.Same(sign, Assert.Single(SignMap.Update(read, scene, []).All));
            DimensionSigns again = SignMap.Update(read, scene, [new Coords(0, 0)]);
            Assert.NotSame(sign, Assert.Single(again.All));
            Assert.Equal([new Coords(0, 0)], again.Read);
        }

        [Fact]
        public void ReadsABedrockWorldByRegion()
        {
            byte[] Record(int x, string text) => Nbt.Compound(new() { ["id"] = "Sign", ["x"] = x, ["y"] = 64, ["z"] = 0, ["Text"] = text });
            using BedrockTestWorld world = new BedrockTestWorld()
                .Chunk(0, 0)
                .Chunk(40, 0)
                .Chunk(0, 0, dimension: 1)
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.BlockEntity), Record(3, "Home"))
                .Put(BedrockTestWorld.Key(40, 0, 0, BedrockChunkTag.BlockEntity), Record(645, "Far"))
                .Put(BedrockTestWorld.Key(0, 0, 1, BedrockChunkTag.BlockEntity), Record(1, "Hot"))
                .Save("Bedrock sign test");
            Level level = WorldLoader.ReadLevel(world.LevelDat);
            SceneInfo overworld = new(level, Dimension.Overworld);

            DimensionSigns read = SignMap.Update(null, overworld, null);
            Assert.Equal(["Home", "Far"], read.All.Select(s => s.Text));
            Assert.Equal("Hot", Assert.Single(SignMap.Read(new SceneInfo(level, Dimension.Nether))).Text);

            // The far region alone read again: the near one's sign is kept as it was
            DimensionSigns again = SignMap.Update(read, overworld, [new Coords(1, 0)]);
            Assert.Same(read.All[0], again.All[0]);
            Assert.NotSame(read.All[1], again.All[1]);
        }

        private static Sign Sign(int x, string text, Dimension? dimension = null) => new(dimension ?? Dimension.Overworld, x, 64, 0, false, [text], []);

        private static DimensionSigns ReadAs(params Sign[] signs) =>
            new(Dimension.Overworld, signs.GroupBy(s => new Coords(s.X >> 9, s.Z >> 9)).ToDictionary(g => g.Key, g => g.ToList()), [new Coords(0, 0)]);

        [Fact]
        public void ShownSignsAreKeptWithTheMarkupAndKeepUp()
        {
            MarkupEditor editor = new(new BlockAPixel());
            int edits = 0;
            editor.Edited += (sender, e) => edits++;

            Assert.Equal(2, editor.ShowSigns([Sign(1, "Home"), Sign(2, "Mine"), Sign(1, "Home")]));
            Assert.Equal(0, editor.ShowSigns([Sign(2, "Mine")]));
            Assert.Equal(1, edits);

            // Not undone with the pins and zones
            editor.Import(new MarkupDocument() { Pins = [new MapPin() { X = 5, Z = 5 }] });
            Assert.True(editor.Undo());
            Assert.Equal(2, editor.Document.Signs.Count);

            // Read again: one now says something else, one is gone, one in a region not read again is left alone
            editor.ShowSigns([Sign(600, "Far")]);
            Assert.True(editor.UpdateSigns(ReadAs(Sign(1, "Home sweet home"))));
            Assert.Equal(["Home sweet home", "Far"], editor.Document.Signs.Select(s => s.Text));
            Assert.False(editor.UpdateSigns(ReadAs(Sign(1, "Home sweet home"))));

            Assert.Equal(1, editor.HideSigns([editor.Document.Signs[0]]));
            Assert.Equal("Far", Assert.Single(editor.Document.Signs).Text);
        }

        [Fact]
        public void ShownSignsAreSavedAndReadBack()
        {
            MarkupStore store = new(_folder);
            string world = Path.Combine(_folder, "worlds", "Signs");
            MarkupDocument document = store.Load(world);
            document.Signs.Add(MapSign.Of(new Sign(Dimension.Nether, 1, 2, 3, true, ["Blaze", "farm"], ["Back"])));
            store.Save(document);

            MapSign sign = Assert.Single(store.Load(world).Signs);
            Assert.Equal((Dimension.Nether.Namespace, 1, 2, 3, true), (sign.Dimension, sign.X, sign.Y, sign.Z, sign.Hanging));
            Assert.Equal(["Blaze", "farm", "Back:", "Back"], sign.Lines);

            // Markup from before signs has none; one shown twice is kept once
            string file = Path.Combine(_folder, "old.json");
            File.WriteAllText(file, """{ "version": 1, "pins": [], "zones": [] }""");
            Assert.Empty(MarkupStore.Read(file).Signs);
            File.WriteAllText(file, """{ "version": 1, "signs": [ { "x": 1, "y": 2, "z": 3, "front": ["A"] }, { "x": 1, "y": 2, "z": 3 } ] }""");
            MapSign read = Assert.Single(MarkupStore.Read(file).Signs);
            Assert.Equal(("minecraft:overworld", "A"), (read.Dimension, read.Title));

            // Brought in from another file only where none is shown already
            MarkupDocument mine = new() { Signs = [read] };
            Assert.Equal(1, mine.MergeSigns(new MarkupDocument() { Signs = [read.Clone(), MapSign.Of(Sign(9, "New"))] }));
        }

        [Fact]
        public void SignLabelsLeaveRoomAndTheHoveredOneShowsAll()
        {
            MarkupEditor editor = new(new BlockAPixel());
            editor.ShowSigns([Sign(100, "Rods"), Sign(101, "Bows"), Sign(200, "Fish"), Sign(5000, "Far away")]);

            MarkupFrame frame = MarkupFrame.Build(editor.Document, editor.View, 400, 400);
            // Off the screen, left out; the sign a block from another has no room for its name
            Assert.Equal([200, 101, 100], frame.Signs.Select(s => s.Sign.X));
            Assert.Equal([true, false, true], frame.Signs.Select(s => s.Labelled));

            editor.PointerMove(101.5, 0.5, false);
            Assert.Equal("Bows", editor.HoveredSign?.Title);
            frame = MarkupFrame.Build(editor.Document, editor.View, 400, 400, editor);
            SignShape hovered = frame.Signs[^1];
            Assert.Equal((101, true, true), (hovered.Sign.X, hovered.Hovered, hovered.Labelled));

            editor.PointerLeft();
            Assert.Null(editor.HoveredSign);
        }
    }
}
