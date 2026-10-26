namespace Mapper
{
    // What to draw for a world's markup on one frame, in screen units, worked out once for both GUIs' painters and
    // the image export: the zones, pins and shown signs and mobs in view, largest zones first, and whatever the editor has half
    // drawn
    public sealed class MarkupFrame
    {
        public List<ZoneShape> Zones { get; } = [];
        public List<PinShape> Pins { get; } = [];
        public List<SignShape> Signs { get; } = [];
        public List<PetShape> Pets { get; } = [];

        // Corners of the zone picked in Select mode, with the one under the mouse
        public List<(double X, double Y)> Handles { get; } = [];
        public int HoveredHandle { get; private set; } = -1;

        // A zone being placed: its corners so far, and the corner the next click would add
        public List<(double X, double Y)> Draft { get; } = [];
        public (double X, double Y)? DraftCursor { get; private set; }
        public bool DraftClosable { get; private set; }

        // A rectangle being dragged, with its size
        public (double Left, double Top, double Right, double Bottom)? Rectangle { get; private set; }
        public string? RectangleText { get; private set; }

        public bool IsEmpty => Zones.Count == 0 && Pins.Count == 0 && Signs.Count == 0 && Pets.Count == 0 && Draft.Count == 0 && Rectangle is null;

        // Pins and zone labels stay readable however far out the map is zoomed; zones too small to see are left out
        private const double MinZoneSize = 2, Margin = 64;
        // Roughly how much room a sign's label takes; signs stand close together, so a label that would cover one
        // already placed is left off until the mouse is over its sign
        private const double CharacterWidth = 7, LabelHeight = 18;

        public static MarkupFrame Build(MarkupDocument document, IMarkupView view, double width, double height, MarkupEditor? editor = null)
        {
            MarkupFrame frame = new();
            string dimension = view.Dimension;

            IEnumerable<MapZone> zones = document.ZonesIn(dimension);
            IEnumerable<MapPin> pins = document.PinsIn(dimension);
            if (editor?.Pending is MapZone pendingZone && pendingZone.Dimension == dimension) zones = zones.Append(pendingZone);
            if (editor?.Pending is MapPin pendingPin && pendingPin.Dimension == dimension) pins = pins.Append(pendingPin);

            foreach (MapZone zone in zones.OrderByDescending(z => ZoneGeometry.Area(z.Points)))
            {
                (double X, double Y)[] points = [.. zone.Points.Select(p => view.ToScreen(p.X, p.Z))];
                double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
                if (maxX < -Margin || maxY < -Margin || minX > width + Margin || minY > height + Margin) continue;
                if (maxX - minX < MinZoneSize && maxY - minY < MinZoneSize) continue;

                bool hovered = editor?.Hovered == zone, selected = editor?.Selected == zone || editor?.Pending == zone;
                var (lx, lz) = ZoneGeometry.LabelPoint(zone.Points);
                frame.Zones.Add(new ZoneShape(zone, points, maxX - minX, maxY - minY, view.ToScreen(lx, lz), hovered, selected,
                    hovered || selected ? ZoneGeometry.Describe(zone.Points) : null));

                if (editor is { Mode: MarkupMode.Select } && editor.Selected == zone)
                {
                    frame.Handles.AddRange(points);
                    frame.HoveredHandle = editor.HoveredCorner;
                }
            }

            foreach (MapPin pin in pins)
            {
                var (x, y) = view.ToScreen(pin.X + 0.5, pin.Z + 0.5);
                if (x < -Margin * 4 || y < -Margin || x > width + Margin || y > height + Margin) continue;
                frame.Pins.Add(new PinShape(pin, x, y, editor?.Hovered == pin, editor?.Selected == pin || editor?.Pending == pin));
            }

            // Which labels fit is worked out over every sign, in view or not, so an image drawn a band at a time
            // labels the same ones in every band
            List<(double Left, double Top, double Right, double Bottom)> labels = [];
            foreach (MapSign sign in document.SignsIn(dimension).OrderByDescending(s => editor?.HoveredSign == s))
            {
                var (x, y) = view.ToScreen(sign.X + 0.5, sign.Z + 0.5);
                bool hovered = editor?.HoveredSign == sign;
                double left = x + MarkupEditor.SignRadius + 4, right = left + sign.Title.Length * CharacterWidth + 10;
                double top = y - LabelHeight / 2, bottom = top + LabelHeight;
                bool labelled = hovered || !labels.Any(l => left < l.Right && right > l.Left && top < l.Bottom && bottom > l.Top);
                if (labelled) labels.Add((left, top, right, bottom));

                if (x < -Margin * 4 || y < -Margin || x > width + Margin || y > height + Margin) continue;
                frame.Signs.Add(new SignShape(sign, x, y, hovered, labelled));
            }
            // The hovered one last, on top
            frame.Signs.Reverse();

            // Mobs' names the same way, around the signs' labels
            foreach (MapPet pet in document.PetsIn(dimension).OrderByDescending(p => editor?.HoveredPet == p))
            {
                var (x, y) = view.ToScreen(pet.X + 0.5, pet.Z + 0.5);
                bool hovered = editor?.HoveredPet == pet;
                double left = x + MarkupEditor.PetRadius + 4, right = left + pet.Name.Length * CharacterWidth + 10;
                double top = y - LabelHeight / 2, bottom = top + LabelHeight;
                bool labelled = hovered || !labels.Any(l => left < l.Right && right > l.Left && top < l.Bottom && bottom > l.Top);
                if (labelled) labels.Add((left, top, right, bottom));

                if (x < -Margin * 4 || y < -Margin || x > width + Margin || y > height + Margin) continue;
                frame.Pets.Add(new PetShape(pet, x, y, hovered, labelled));
            }
            frame.Pets.Reverse();

            if (editor is null) return frame;

            frame.Draft.AddRange(editor.Draft.Select(p => view.ToScreen(p.X, p.Z)));
            if (editor.Mode == MarkupMode.Zone && editor.DraftCursor is MarkupPoint cursor && editor.Mouse is not null)
            {
                frame.DraftCursor = view.ToScreen(cursor.X, cursor.Z);
            }
            frame.DraftClosable = editor.Draft.Count >= 3;

            if (editor.RectanglePoints() is List<MarkupPoint> corners)
            {
                var topLeft = view.ToScreen(corners[0].X, corners[0].Z);
                var bottomRight = view.ToScreen(corners[2].X, corners[2].Z);
                frame.Rectangle = (topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                frame.RectangleText = ZoneGeometry.Describe(corners);
            }
            return frame;
        }
    }

    // ScreenWidth and ScreenHeight are the zone's extent on screen, which says whether its label fits. Detail is its
    // size, given while the mouse is over it or it's picked
    public record ZoneShape(MapZone Zone, (double X, double Y)[] Points, double ScreenWidth, double ScreenHeight, (double X, double Y) LabelAt,
        bool Hovered, bool Selected, string? Detail);

    public record PinShape(MapPin Pin, double X, double Y, bool Hovered, bool Selected);

    // A shown sign's mark at its block; Labelled when there's room for its first line. Hovered shows all its lines
    public record SignShape(MapSign Sign, double X, double Y, bool Hovered, bool Labelled)
    {
        // The board, centred a little above the block's middle, and its post below
        public (double Left, double Top, double Width, double Height) Board => (X - BoardWidth / 2, Y - BoardHeight / 2 - 2, BoardWidth, BoardHeight);
        public (double Left, double Top, double Width, double Height) Post => (X - 1, Y + BoardHeight / 2 - 2, 2, 5);

        public const double BoardWidth = 14, BoardHeight = 10;
        public const string Wood = "#c8a165";
    }

    // A shown named mob's mark where it was last saved: a paw on a pale disc; Labelled when there's room for its name.
    // Hovered shows what it is and where
    public record PetShape(MapPet Pet, double X, double Y, bool Hovered, bool Labelled)
    {
        public const string Fur = "#f3e3c3", PawColor = "#6d4c41";
        // In a 16 box, as the pin symbols are: the pad and four toes
        public const string Paw = "M8 8.3 C10.7 8.3 12.6 10.7 12.6 12.6 C12.6 14.3 11.2 14.9 10 14.5 C9.2 14.2 8.6 14 8 14 C7.4 14 6.8 14.2 6 14.5 C4.8 14.9 3.4 14.3 3.4 12.6 C3.4 10.7 5.3 8.3 8 8.3 Z " +
            "M1 7.4 A1.6 2.1 0 1 0 4.2 7.4 A1.6 2.1 0 1 0 1 7.4 Z M4.1 3.5 A1.6 2.1 0 1 0 7.3 3.5 A1.6 2.1 0 1 0 4.1 3.5 Z " +
            "M8.7 3.5 A1.6 2.1 0 1 0 11.9 3.5 A1.6 2.1 0 1 0 8.7 3.5 Z M11.8 7.4 A1.6 2.1 0 1 0 15 7.4 A1.6 2.1 0 1 0 11.8 7.4 Z";

        // While the mouse is over it: its name, what it is, and the block it was on
        public IReadOnlyList<string> Lines => [Pet.Name, Pet.Description, $"{Pet.X}, {Pet.Y}, {Pet.Z}"];
    }
}
