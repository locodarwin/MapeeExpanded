using Mapper;

namespace Mapee.Tests.Markup
{
    // Pins and zones: their sums, how they're kept, and what clicks, drags and keys do to them in the editor
    public class MarkupTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-markup-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        // Two screen units a block, with block 0, 0 at screen 100, 100
        private class TestView : IMarkupView
        {
            public string Dimension { get; set; } = "minecraft:overworld";
            public (double X, double Y) ToScreen(double x, double z) => (100 + x * 2, 100 + z * 2);
            public (double X, double Z) ToWorld(double x, double y) => ((x - 100) / 2, (y - 100) / 2);
        }

        private static List<MarkupPoint> Square(int size) => MapZone.Rectangle(0, 0, size - 1, size - 1);

        [Fact]
        public void AZoneMeasuresInBlocksAndChunks()
        {
            Assert.Equal(256, ZoneGeometry.Area(Square(16)));
            Assert.Equal("16 × 16 blocks, 1 chunk", ZoneGeometry.Describe(Square(16)));
            Assert.Equal("48 × 32 blocks, 6 chunks", ZoneGeometry.Describe(MapZone.Rectangle(0, 0, 47, 31)));

            List<MarkupPoint> triangle = [new(0, 0), new(10, 0), new(0, 10)];
            Assert.Equal(50, ZoneGeometry.Area(triangle));
            Assert.Equal("50 blocks, 0.2 chunks", ZoneGeometry.Describe(triangle));
        }

        [Fact]
        public void ARectangleHoldsBothCornerBlocks()
        {
            List<MarkupPoint> rectangle = MapZone.Rectangle(5, 5, -2, 0);
            Assert.Equal([new(-2, 0), new(6, 0), new(6, 6), new(-2, 6)], rectangle);
            Assert.True(ZoneGeometry.IsRectangle(rectangle));
            Assert.False(ZoneGeometry.IsRectangle([new(0, 0), new(4, 0), new(4, 4), new(1, 4)]));
        }

        [Fact]
        public void ConcaveZonesKeepTheirLabelInside()
        {
            // A U whose centroid falls in the gap between its arms
            List<MarkupPoint> u = [new(0, 0), new(10, 0), new(10, 10), new(7, 10), new(7, 2), new(3, 2), new(3, 10), new(0, 10)];
            Assert.True(ZoneGeometry.Contains(u, 1, 5));
            Assert.False(ZoneGeometry.Contains(u, 5, 5));

            var (x, z) = ZoneGeometry.LabelPoint(u);
            Assert.True(ZoneGeometry.Contains(u, x, z));

            Assert.Equal((5.0, 5.0), ZoneGeometry.LabelPoint(Square(10)));
        }

        [Fact]
        public void MarkupIsKeptPerWorldAndReadBack()
        {
            MarkupStore store = new(_folder);
            string world = Path.Combine(_folder, "saves", "My World!");

            MarkupDocument document = store.Load(world);
            Assert.True(document.IsEmpty);

            document.Pins.Add(new MapPin() { X = -5, Z = 12, Label = "Home", Icon = "home", Color = "#43A047", Note = "bed" });
            document.Zones.Add(new MapZone() { Label = "Farm", Dimension = "minecraft:nether", Points = Square(32) });
            store.Save(document);

            string file = store.FileFor(world);
            Assert.StartsWith("My World-", Path.GetFileName(file));
            Assert.Equal(file, store.FileFor(Path.Combine(world, "level.dat")));
            Assert.NotEqual(file, store.FileFor(world + " copy"));

            MarkupDocument read = store.Load(world);
            MapPin pin = Assert.Single(read.Pins);
            Assert.Equal((-5, 12, "Home", "home", "#43a047", "bed"), (pin.X, pin.Z, pin.Label, pin.Icon, pin.Color, pin.Note));
            MapZone zone = Assert.Single(read.Zones);
            Assert.Equal(("Farm", "minecraft:nether"), (zone.Label, zone.Dimension));
            Assert.Equal(Square(32), zone.Points);

            // A world with nothing left marked has no file
            read.Pins.Clear();
            read.Zones.Clear();
            store.Save(read);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void TheFileIsNamedForTheWorldAndFoundByItsPath()
        {
            MarkupStore store = new(_folder);
            string world = Path.Combine(_folder, "saves", "x+HPe5HdJCc=");

            MarkupDocument document = store.Load(world, "Myopic");
            document.Pins.Add(new MapPin() { Label = "Home" });
            store.Save(document);
            Assert.StartsWith("Myopic-", Path.GetFileName(store.FileFor(world)));

            // Renamed in the game, the world keeps its markup and its file
            MarkupDocument renamed = store.Load(world, "Myopic Two");
            Assert.Equal("Home", Assert.Single(renamed.Pins).Label);
            store.Save(renamed);
            Assert.Single(Directory.GetFiles(_folder, "*.json"));
        }

        [Fact]
        public void AnUnreadableFileIsSetAsideNotOverwritten()
        {
            MarkupStore store = new(_folder);
            string world = Path.Combine(_folder, "world");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(store.FileFor(world), "{ not json");

            Assert.True(store.Load(world).IsEmpty);
            Assert.True(File.Exists(store.FileFor(world) + ".unreadable"));
        }

        [Fact]
        public void FilesFromElsewhereAreTidiedAsTheyAreRead()
        {
            string file = Path.Combine(_folder, "import.json");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(file, """
                { "pins": [ { "x": 1, "z": 2, "color": "pink", "icon": "rocket" } ],
                  "zones": [ { "label": "Line", "points": [ { "x": 0, "z": 0 }, { "x": 5, "z": 5 } ] } ] }
                """);

            MarkupDocument document = MarkupStore.Read(file);
            MapPin pin = Assert.Single(document.Pins);
            Assert.Equal((MarkupColors.Default, null, "", "minecraft:overworld"), (pin.Color, pin.Icon, pin.Label, pin.Dimension));
            Assert.False(string.IsNullOrEmpty(pin.Id));
            Assert.Empty(document.Zones);
        }

        [Fact]
        public void MergingBringsInOnlyWhatIsMissing()
        {
            MarkupDocument mine = new();
            MapPin home = new() { Label = "Home" };
            mine.Pins.Add(home);

            MarkupDocument theirs = new();
            theirs.Pins.Add(home.Clone());
            theirs.Pins.Add(new MapPin() { Label = "Mine" });
            theirs.Zones.Add(new MapZone() { Label = "Town", Points = Square(8) });

            Assert.Equal((1, 1), mine.Merge(theirs));
            Assert.Equal(["Home", "Mine"], mine.Pins.Select(p => p.Label));
            Assert.Equal((0, 0), mine.Merge(theirs));
        }

        private static (MarkupEditor Editor, List<MarkupEditRequest> Requests) NewEditor(MarkupMode mode)
        {
            MarkupEditor editor = new(new TestView()) { Mode = mode };
            List<MarkupEditRequest> requests = [];
            editor.EditRequested += (sender, request) => requests.Add(request);
            return (editor, requests);
        }

        private static void ClickAt(MarkupEditor editor, double x, double y, bool shift = false, MarkupButton button = MarkupButton.Left)
        {
            editor.PointerDown(x, y, button, shift);
            editor.PointerUp(x, y, shift);
        }

        [Fact]
        public void AClickDropsAPinThatWaitsForItsDetails()
        {
            var (editor, requests) = NewEditor(MarkupMode.Pin);
            int saves = 0;
            editor.Edited += (sender, e) => saves++;

            // Block 10, -3 covers screen 120 to 122 across and 94 to 96 down
            ClickAt(editor, 121, 95);
            MarkupEditRequest request = Assert.Single(requests);
            Assert.True(request.IsNew);
            MapPin pin = Assert.IsType<MapPin>(request.Item);
            Assert.Equal((10, -3), (pin.X, pin.Z));
            Assert.Same(pin, editor.Pending);
            Assert.Empty(editor.Document.Pins);

            pin.Label = "Tower";
            pin.Color = "#1e88e5";
            pin.Icon = "base";
            editor.Commit(pin);
            Assert.Same(pin, Assert.Single(editor.Document.Pins));
            Assert.Null(editor.Pending);
            Assert.Equal(1, saves);

            // The next pin starts with the same look
            ClickAt(editor, 161, 161);
            MapPin next = Assert.IsType<MapPin>(requests[^1].Item);
            Assert.Equal(("#1e88e5", "base"), (next.Color, next.Icon));
            editor.Cancel(next);
            Assert.Null(editor.Pending);
            Assert.Single(editor.Document.Pins);
        }

        [Fact]
        public void ClickingAnExistingPinEditsACopy()
        {
            var (editor, requests) = NewEditor(MarkupMode.Pin);
            MapPin pin = new() { X = 0, Z = 0, Label = "Spawn" };
            editor.Document.Pins.Add(pin);

            ClickAt(editor, 101, 101);
            MarkupEditRequest request = Assert.Single(requests);
            Assert.False(request.IsNew);
            Assert.NotSame(pin, request.Item);

            request.Item.Label = "World spawn";
            editor.Commit(request.Item);
            Assert.Equal("World spawn", pin.Label);
            Assert.Single(editor.Document.Pins);

            Assert.True(editor.Undo());
            Assert.Equal("Spawn", editor.Document.Pins[0].Label);
            Assert.True(editor.Redo());
            Assert.Equal("World spawn", editor.Document.Pins[0].Label);
        }

        [Fact]
        public void ADragPansInsteadOfDroppingAPin()
        {
            var (editor, requests) = NewEditor(MarkupMode.Pin);
            Assert.False(editor.PointerDown(100, 100, MarkupButton.Left, false));
            editor.PointerMove(140, 100, false);
            editor.PointerUp(140, 100, false);
            Assert.Empty(requests);
        }

        [Fact]
        public void ZoneCornersSnapAndADoubleClickClosesTheZone()
        {
            var (editor, requests) = NewEditor(MarkupMode.Zone);
            DateTime now = new(2026, 1, 1);
            editor.Clock = () => now;

            // Corners round to the nearest block corner: screen 101 is x 0.5, which rounds to the even 0
            ClickAt(editor, 101, 100);
            now += TimeSpan.FromSeconds(1);
            ClickAt(editor, 141, 99);
            now += TimeSpan.FromSeconds(1);
            ClickAt(editor, 140, 141);
            Assert.Equal([new(0, 0), new(20, 0), new(20, 20)], editor.Draft);

            // Backspace takes the last corner back
            Assert.True(editor.Key(MarkupKey.Backspace));
            Assert.Equal(2, editor.Draft.Count);

            now += TimeSpan.FromSeconds(1);
            ClickAt(editor, 100, 140);
            now += TimeSpan.FromMilliseconds(200);
            ClickAt(editor, 100, 140);

            MapZone zone = Assert.IsType<MapZone>(Assert.Single(requests).Item);
            Assert.Equal([new(0, 0), new(20, 0), new(0, 20)], zone.Points);
            Assert.Empty(editor.Draft);
        }

        [Fact]
        public void AZoneClosesOnItsFirstCornerAndSnapsToChunksWithShift()
        {
            var (editor, requests) = NewEditor(MarkupMode.Zone);
            DateTime now = new(2026, 1, 1);
            editor.Clock = () => now += TimeSpan.FromSeconds(1);

            ClickAt(editor, 105, 103, shift: true);
            ClickAt(editor, 135, 103, shift: true);
            ClickAt(editor, 135, 165, shift: true);
            ClickAt(editor, 98, 165, shift: true);
            ClickAt(editor, 102, 101);

            MapZone zone = Assert.IsType<MapZone>(Assert.Single(requests).Item);
            Assert.Equal([new(0, 0), new(16, 0), new(16, 32), new(0, 32)], zone.Points);
        }

        [Fact]
        public void ARectangleIsDraggedOut()
        {
            var (editor, requests) = NewEditor(MarkupMode.Rectangle);
            Assert.True(editor.PointerDown(101, 101, MarkupButton.Left, false));
            editor.PointerMove(131, 111, false);
            Assert.Equal((new MarkupPoint(0, 0), new MarkupPoint(15, 5)), editor.RectangleDrag);
            editor.PointerUp(131, 111, false);

            MapZone zone = Assert.IsType<MapZone>(Assert.Single(requests).Item);
            Assert.Equal(MapZone.Rectangle(0, 0, 15, 5), zone.Points);
            Assert.Null(editor.RectangleDrag);
        }

        [Fact]
        public void ARectangleWithShiftTakesInWholeChunks()
        {
            var (editor, requests) = NewEditor(MarkupMode.Rectangle);
            editor.PointerDown(105, 105, MarkupButton.Left, true);
            editor.PointerMove(141, 111, true);
            editor.PointerUp(141, 111, true);

            MapZone zone = Assert.IsType<MapZone>(Assert.Single(requests).Item);
            Assert.Equal(MapZone.Rectangle(0, 0, 31, 15), zone.Points);
        }

        [Fact]
        public void DoubleClicksOnAPickedZoneAddAndRemoveCorners()
        {
            var (editor, _) = NewEditor(MarkupMode.Select);
            DateTime now = new(2026, 1, 1);
            editor.Clock = () => now;
            MapZone zone = new() { Points = MapZone.Rectangle(0, 0, 19, 19) };
            editor.Document.Zones.Add(zone);
            ClickAt(editor, 120, 120);
            Assert.Same(zone, editor.Selected);

            // The top edge runs along screen y 100; a double-click there adds a corner at x 10
            now += TimeSpan.FromSeconds(1);
            ClickAt(editor, 120, 101);
            now += TimeSpan.FromMilliseconds(100);
            ClickAt(editor, 120, 101);
            Assert.Equal([new(0, 0), new(10, 0), new(20, 0), new(20, 20), new(0, 20)], zone.Points);

            // and on that corner, takes it away again
            now += TimeSpan.FromSeconds(1);
            ClickAt(editor, 120, 100);
            now += TimeSpan.FromMilliseconds(100);
            ClickAt(editor, 120, 100);
            Assert.Equal(MapZone.Rectangle(0, 0, 19, 19), zone.Points);

            Assert.True(editor.Undo());
            Assert.Equal(5, editor.Document.Zones[0].Points.Count);
        }

        [Fact]
        public void SelectModeDragsPinsAndCornersAndUndoPutsThemBack()
        {
            var (editor, requests) = NewEditor(MarkupMode.Select);
            MapPin pin = new() { X = 0, Z = 0 };
            MapZone zone = new() { Points = MapZone.Rectangle(10, 10, 19, 19) };
            editor.Document.Pins.Add(pin);
            editor.Document.Zones.Add(zone);

            Assert.True(editor.PointerDown(101, 101, MarkupButton.Left, false));
            editor.PointerMove(111, 121, false);
            editor.PointerUp(111, 121, false);
            Assert.Equal((5, 10), (editor.Document.Pins[0].X, editor.Document.Pins[0].Z));

            // A click inside a zone picks it; then its corners can be dragged
            ClickAt(editor, 130, 130);
            Assert.Same(zone, editor.Selected);
            Assert.True(editor.PointerDown(140, 140, MarkupButton.Left, false));
            editor.PointerMove(150, 146, false);
            editor.PointerUp(150, 146, false);
            Assert.Equal(new MarkupPoint(25, 23), zone.Points[2]);

            Assert.True(editor.Undo());
            Assert.Equal(new MarkupPoint(20, 20), editor.Document.Zones[0].Points[2]);
            Assert.True(editor.Undo());
            Assert.Equal((0, 0), (editor.Document.Pins[0].X, editor.Document.Pins[0].Z));
            Assert.False(editor.Undo());
            Assert.Empty(requests);
        }

        [Fact]
        public void RightClickEditsAndDeleteRemoves()
        {
            var (editor, requests) = NewEditor(MarkupMode.Select);
            MapZone big = new() { Label = "Big", Points = Square(50) };
            MapZone small = new() { Label = "Small", Points = MapZone.Rectangle(10, 10, 14, 14) };
            editor.Document.Zones.AddRange([big, small]);

            // The smaller zone lies over the larger one
            ClickAt(editor, 124, 124, button: MarkupButton.Right);
            Assert.Equal("Small", Assert.Single(requests).Item.Label);

            Assert.True(editor.Key(MarkupKey.Delete));
            Assert.Equal("Big", Assert.Single(editor.Document.Zones).Label);
            Assert.True(editor.Undo());
            Assert.Equal(2, editor.Document.Zones.Count);
        }

        [Fact]
        public void OnlyTheDimensionOnShowIsHitOrDrawn()
        {
            TestView view = new();
            MarkupEditor editor = new(view) { Mode = MarkupMode.Select };
            editor.Document.Pins.Add(new MapPin() { X = 0, Z = 0, Dimension = "minecraft:nether" });
            editor.Document.Zones.Add(new MapZone() { Points = Square(10) });

            Assert.Null(editor.PinAt(101, 101));
            MarkupFrame frame = MarkupFrame.Build(editor.Document, view, 800, 600, editor);
            Assert.Empty(frame.Pins);
            Assert.Single(frame.Zones);

            view.Dimension = "minecraft:nether";
            frame = MarkupFrame.Build(editor.Document, view, 800, 600, editor);
            Assert.Single(frame.Pins);
            Assert.Empty(frame.Zones);
        }

        [Fact]
        public void EscapeDropsAHalfPlacedZone()
        {
            var (editor, _) = NewEditor(MarkupMode.Zone);
            ClickAt(editor, 100, 100);
            Assert.Single(editor.Draft);
            Assert.True(editor.Key(MarkupKey.Escape));
            Assert.Empty(editor.Draft);
            Assert.False(editor.Key(MarkupKey.Escape));
        }
    }
}
