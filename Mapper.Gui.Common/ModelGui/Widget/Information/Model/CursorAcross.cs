using WorldEditor;

namespace Mapper.Gui.Model
{
    // Where the block under the cursor is in the other dimension a Nether portal leads to: "; Nether: (12; -40)"
    public static class CursorAcross
    {
        // The words before the place, or null where portals don't lead
        public static string? Prefix(Dimension? dimension)
        {
            if (dimension == Dimension.Overworld) return "; Nether: (";
            if (dimension == Dimension.Nether) return "; Overworld: (";
            return null;
        }

        public static XzPoint Place(Dimension dimension, XzPoint point)
        {
            var (x, z) = PortalMap.Across(dimension, point.X, point.Z);
            return new XzPoint(x, z);
        }
    }
}
