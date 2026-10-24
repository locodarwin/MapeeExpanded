namespace Mapper
{
    // A structure on the screen: its middle, its box when that's big enough to see, whether its name fits, and on
    // which side of the mark (the left near the screen's right edge). The label starts, or ends, MarkGap from the mark
    public record StructureShape(Structure Structure, double X, double Y, (double Left, double Top, double Right, double Bottom)? Box, bool Labelled, bool LabelOnLeft)
    {
        public string Color => Structure.Kind.Color;
        public const double MarkGap = StructureFrame.MarkSize / 2 + 4;
    }

    // Where the structures of the dimension on show go on the screen, for either GUI's painter to draw. Every
    // structure gets a mark; names go on in order of how much the kind matters, leaving out any that would cover one
    // already placed
    public class StructureFrame
    {
        public const double MarkSize = 10;
        // A box smaller than this on the screen is left to the mark
        public const double SmallestBox = 14;
        // Roughly how much room a label takes
        private const double CharacterWidth = 7, LabelHeight = 20, LabelGap = 2;

        public List<StructureShape> Shapes { get; } = [];

        // structures in the order they matter, as StructureMap.Read sorts them
        public static StructureFrame Build(IEnumerable<Structure> structures, IMarkupView view, double width, double height)
        {
            StructureFrame frame = new();
            var (originX, _) = view.ToScreen(0, 0);
            if (view.ToScreen(1, 0).X - originX <= 0) return frame;

            const double margin = 200;
            List<(double Left, double Top, double Right, double Bottom)> labels = [];
            foreach (Structure structure in structures)
            {
                var (left, top) = view.ToScreen(structure.MinX, structure.MinZ);
                var (right, bottom) = view.ToScreen(structure.MaxX + 1, structure.MaxZ + 1);
                if (right < -margin || bottom < -margin || left > width + margin || top > height + margin) continue;

                var (x, y) = view.ToScreen(structure.X + 0.5, structure.Z + 0.5);
                bool boxed = right - left >= SmallestBox || bottom - top >= SmallestBox;

                double labelWidth = structure.Name.Length * CharacterWidth + 10;
                bool onLeft = x + StructureShape.MarkGap + labelWidth > width;
                double labelLeft = onLeft ? x - StructureShape.MarkGap - labelWidth : x + StructureShape.MarkGap, labelTop = y - LabelHeight / 2;
                double labelRight = labelLeft + labelWidth, labelBottom = labelTop + LabelHeight;
                bool labelled = x >= 0 && y >= 0 && x <= width && y <= height
                    && !labels.Any(l => labelLeft < l.Right + LabelGap && labelRight + LabelGap > l.Left && labelTop < l.Bottom + LabelGap && labelBottom + LabelGap > l.Top);
                if (labelled) labels.Add((labelLeft, labelTop, labelRight, labelBottom));

                frame.Shapes.Add(new StructureShape(structure, x, y, boxed ? (left, top, right, bottom) : null, labelled, onLeft));
            }
            return frame;
        }
    }
}
