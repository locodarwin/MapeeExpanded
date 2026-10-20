namespace Mapper
{
    // What to draw for a world's markup on one frame, in screen units, worked out once for both GUIs' painters and
    // the image export: the zones and pins in view, largest zones first, and whatever the editor has half drawn
    public sealed class MarkupFrame
    {
        public List<ZoneShape> Zones { get; } = [];
        public List<PinShape> Pins { get; } = [];

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

        public bool IsEmpty => Zones.Count == 0 && Pins.Count == 0 && Draft.Count == 0 && Rectangle is null;

        // Pins and zone labels stay readable however far out the map is zoomed; zones too small to see are left out
        private const double MinZoneSize = 2, Margin = 64;

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
}
