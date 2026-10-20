namespace Mapper
{
    // Measuring zones and finding what lies under a point. Zones are polygons on the block-corner grid; they may be
    // concave, and an edge may not cross another (the editor doesn't stop that, and the sums still give sensible answers)
    public static class ZoneGeometry
    {
        // In blocks; a 16 by 16 square is 256
        public static double Area(IReadOnlyList<MarkupPoint> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MarkupPoint a = points[i], b = points[(i + 1) % points.Count];
                sum += (double)a.X * b.Z - (double)b.X * a.Z;
            }
            return Math.Abs(sum) / 2;
        }

        public static (int MinX, int MinZ, int MaxX, int MaxZ) Bounds(IReadOnlyList<MarkupPoint> points)
        {
            return (points.Min(p => p.X), points.Min(p => p.Z), points.Max(p => p.X), points.Max(p => p.Z));
        }

        // Whether a point in block coordinates (fractions allowed) lies inside, by the even-odd rule
        public static bool Contains(IReadOnlyList<MarkupPoint> points, double x, double z)
        {
            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                MarkupPoint a = points[i], b = points[j];
                if ((a.Z > z) != (b.Z > z) && x < (b.X - a.X) * (z - a.Z) / (double)(b.Z - a.Z) + a.X) inside = !inside;
            }
            return inside;
        }

        // A zone's size in words: "48 × 32 blocks, 6 chunks" for a rectangle, "1,536 blocks, 6 chunks" otherwise
        public static string Describe(IReadOnlyList<MarkupPoint> points)
        {
            double area = Area(points);
            double chunks = area / 256;
            string chunkText = chunks == Math.Floor(chunks) ? $"{chunks:N0} chunk{(chunks == 1 ? "" : "s")}" : $"{chunks:N1} chunks";

            if (IsRectangle(points))
            {
                var (minX, minZ, maxX, maxZ) = Bounds(points);
                return $"{maxX - minX:N0} × {maxZ - minZ:N0} blocks, {chunkText}";
            }
            return $"{area:N0} blocks, {chunkText}";
        }

        public static bool IsRectangle(IReadOnlyList<MarkupPoint> points)
        {
            if (points.Count != 4) return false;
            var (minX, minZ, maxX, maxZ) = Bounds(points);
            return points.All(p => (p.X == minX || p.X == maxX) && (p.Z == minZ || p.Z == maxZ)) && Area(points) == (double)(maxX - minX) * (maxZ - minZ);
        }

        // Where a zone's label goes: its centroid when that lies inside, which it does for any convex zone; otherwise
        // the middle of the widest stretch of the zone along the line through the centroid
        public static (double X, double Z) LabelPoint(IReadOnlyList<MarkupPoint> points)
        {
            double signed = 0, cx = 0, cz = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MarkupPoint a = points[i], b = points[(i + 1) % points.Count];
                double cross = (double)a.X * b.Z - (double)b.X * a.Z;
                signed += cross;
                cx += (a.X + b.X) * cross;
                cz += (a.Z + b.Z) * cross;
            }

            if (signed == 0)
            {
                var (minX, minZ, maxX, maxZ) = Bounds(points);
                return ((minX + maxX) / 2.0, (minZ + maxZ) / 2.0);
            }

            cx /= 3 * signed;
            cz /= 3 * signed;
            if (Contains(points, cx, cz)) return (cx, cz);

            // The stretches of the line z = cz inside the zone lie between pairs of its crossings with the edges
            List<double> crossings = [];
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                MarkupPoint a = points[i], b = points[j];
                if ((a.Z > cz) != (b.Z > cz)) crossings.Add((b.X - a.X) * (cz - a.Z) / (b.Z - a.Z) + a.X);
            }
            crossings.Sort();

            double bestWidth = -1, bestX = cx;
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                double width = crossings[i + 1] - crossings[i];
                if (width > bestWidth)
                {
                    bestWidth = width;
                    bestX = (crossings[i] + crossings[i + 1]) / 2;
                }
            }
            return (bestX, cz);
        }

        // Distance from a point to the segment between a and b, in whatever units the three are given in
        public static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
            double x = ax + t * dx - px, y = ay + t * dy - py;
            return Math.Sqrt(x * x + y * y);
        }
    }
}
