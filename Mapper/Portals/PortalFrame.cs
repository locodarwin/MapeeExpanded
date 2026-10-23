using WorldEditor;

namespace Mapper
{
    public enum PortalState
    {
        // Leads to a portal that leads back
        Paired,
        // Nothing near enough on the other side: the game would build a portal there
        Builds,
        // Leads to a portal that leads somewhere else
        Mismatched
    }

    // A portal on the screen: its middle, the size of its mark, what it says, and where the portal it leads to stands
    // as seen from this dimension (a Nether portal's place times 8, or an Overworld one's divided by 8), if there is one.
    // The label starts at LabelX, its lines centred on LabelY
    public record PortalShape(Portal Portal, double X, double Y, double Size, PortalState State, string Label, string? Detail, (double X, double Y)? Partner)
    {
        public double LabelX { get; init; }
        public double LabelY { get; init; }
    }

    // Where the portals of the dimension on show go on the screen, for either GUI's painter to draw
    public class PortalFrame
    {
        private const double MinSize = 10;
        // Roughly how much room a label takes, to keep labels of portals close together apart
        private const double CharacterWidth = 7, LineHeight = 16, LabelGap = 2;

        public List<PortalShape> Portals { get; } = [];

        public static readonly IReadOnlyDictionary<PortalState, string> Colors = new Dictionary<PortalState, string>
        {
            [PortalState.Paired] = "#c25bff",
            [PortalState.Builds] = "#ffb300",
            [PortalState.Mismatched] = "#ff3b3b"
        };

        public static PortalFrame Build(PortalMap map, Dimension dimension, IMarkupView view, double width, double height)
        {
            PortalFrame frame = new();
            var (originX, _) = view.ToScreen(0, 0);
            double zoom = view.ToScreen(1, 0).X - originX;
            if (zoom <= 0) return frame;

            Dimension other = dimension == Dimension.Nether ? Dimension.Overworld : Dimension.Nether;
            double margin = 200;
            foreach (Portal portal in map.In(dimension))
            {
                var (x, y) = view.ToScreen(portal.X + 0.5, portal.Z + 0.5);
                PortalLink link = map.Link(portal);

                (double, double)? partner = null;
                if (link.To is Portal to)
                {
                    var (px, pz) = PortalMap.Across(other, to.X + 0.5, to.Z + 0.5);
                    partner = view.ToScreen(px + 0.5, pz + 0.5);
                }

                bool near = x > -margin && y > -margin && x < width + margin && y < height + margin;
                bool partnerNear = partner is var (sx, sy) && sx > -margin && sy > -margin && sx < width + margin && sy < height + margin;
                if (!near && !partnerNear) continue;

                PortalState state = link.To is null ? PortalState.Builds : link.Mismatched ? PortalState.Mismatched : PortalState.Paired;
                string label = link.To is Portal found
                    ? $"→ {other.Name} {found}"
                    : $"→ {other.Name}: a new portal near {link.TargetX}, {link.TargetZ}";
                string? detail = link.Mismatched && link.Back is Portal back ? $"which leads back to {back}, not here" : null;

                double size = Math.Max(MinSize, zoom * 2);
                frame.Portals.Add(new PortalShape(portal, x, y, size, state, label, detail, partner) { LabelX = x + size / 2 + 4, LabelY = y });
            }

            // Mismatched ones are drawn last, on top; labels lower down move down out of the way of those above
            frame.Portals.Sort((a, b) => a.State != b.State ? a.State.CompareTo(b.State) : a.Y.CompareTo(b.Y));
            List<(double Left, double Top, double Right, double Bottom)> placed = [];
            for (int i = 0; i < frame.Portals.Count; i++)
            {
                PortalShape shape = frame.Portals[i];
                double labelWidth = Math.Max(shape.Label.Length, shape.Detail?.Length ?? 0) * CharacterWidth + 10;
                double labelHeight = (shape.Detail is null ? 1 : 2) * LineHeight + 4;
                double top = shape.LabelY - labelHeight / 2;
                bool moved = true;
                while (moved)
                {
                    moved = false;
                    foreach (var (left, upper, right, bottom) in placed)
                    {
                        if (shape.LabelX < right && shape.LabelX + labelWidth > left && top < bottom && top + labelHeight > upper)
                        {
                            top = bottom + LabelGap;
                            moved = true;
                        }
                    }
                }
                placed.Add((shape.LabelX, top, shape.LabelX + labelWidth, top + labelHeight));
                frame.Portals[i] = shape with { LabelY = top + labelHeight / 2 };
            }
            return frame;
        }
    }
}
