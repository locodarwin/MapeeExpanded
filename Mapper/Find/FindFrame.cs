using WorldEditor;

namespace Mapper
{
    // The colour each of a search's targets is marked in, in the order they were chosen; bright, to stand out on the map
    public static class FindColors
    {
        public static readonly IReadOnlyList<string> All = ["#00e5ff", "#ffd600", "#ff1744", "#76ff03", "#d500f9", "#ff9100", "#2979ff", "#ffffff"];

        public static string Of(int target) => All[target % All.Count];

        // The markup colour nearest each, for a pin put on what was found
        private static readonly string[] PinColors = ["Cyan", "Yellow", "Red", "Green", "Purple", "Orange", "Blue", "White"];

        public static string PinColor(int target)
        {
            string name = PinColors[target % PinColors.Length];
            return MarkupColors.All.First(c => c.Name == name).Hex;
        }
    }

    // A mark on the screen: a chunk's square, or a found block's dot. X and Y are its top-left corner
    public readonly record struct FindMark(double X, double Y, double Size, int Target, double Opacity);

    // Where a search's matches go on the screen. Zoomed out, each chunk holding any gets a square in its middle for
    // each target it holds, bigger and more solid the more it holds, the rarest targets on top so a spawner isn't lost
    // among diamonds; the map shows between them. Zoomed in far enough to tell blocks apart, each found block is a dot.
    // Each GUI's painter only draws it
    public class FindFrame
    {
        // Pixels a block takes on the screen from which found blocks are shown one by one
        public const double DotZoom = 2;
        private const double MinSquare = 3, MinDot = 5;

        public List<FindMark> Marks { get; } = [];

        // Whether the marks are single blocks rather than chunks
        public bool Dots { get; private set; }

        // A ring around the cluster picked in the list: centre and radius
        public (double X, double Y, double Radius)? Ring { get; private set; }

        public static FindFrame Build(FindResults results, IMarkupView view, double width, double height, FoundCluster? picked = null)
        {
            FindFrame frame = new();

            var (originX, originY) = view.ToScreen(0, 0);
            double zoom = view.ToScreen(1, 0).X - originX;
            if (zoom <= 0) return frame;

            var (left, top) = view.ToWorld(0, 0);
            var (right, bottom) = view.ToWorld(width, height);
            int blockLeft = (int)Math.Floor(left) - 1, blockTop = (int)Math.Floor(top) - 1;
            int blockRight = (int)Math.Ceiling(right) + 1, blockBottom = (int)Math.Ceiling(bottom) + 1;

            if (zoom >= DotZoom)
            {
                frame.Dots = true;
                double size = Math.Max(zoom, MinDot), inset = (zoom - size) / 2;
                HashSet<(int, int, int)> drawn = [];
                foreach (BlockMatch match in results.MatchesIn(blockLeft, blockTop, blockRight, blockBottom))
                {
                    // One dot per column, whatever the heights below it
                    if (!drawn.Add((match.X, match.Z, match.Target))) continue;
                    var (x, y) = view.ToScreen(match.X, match.Z);
                    frame.Marks.Add(new FindMark(x + inset, y + inset, size, match.Target, 1));
                }
            }
            else
            {
                double chunk = 16 * zoom;
                int chunkLeft = MathUtilities.FindSectionY(blockLeft, 16), chunkTop = MathUtilities.FindSectionY(blockTop, 16);
                int chunkRight = MathUtilities.FindSectionY(blockRight, 16), chunkBottom = MathUtilities.FindSectionY(blockBottom, 16);

                // Most common first, so the rarer are drawn over them
                IReadOnlyList<int> totals = results.Counts;
                int[] order = [.. Enumerable.Range(0, totals.Count).OrderByDescending(i => totals[i])];

                foreach (var (coords, counts) in results.Chunks)
                {
                    if (coords.X < chunkLeft || coords.X > chunkRight || coords.Z < chunkTop || coords.Z > chunkBottom) continue;
                    var (x, y) = view.ToScreen(coords.X * 16, coords.Z * 16);
                    foreach (int target in order)
                    {
                        if (counts[target] == 0) continue;
                        double size = Math.Max(MinSquare, chunk * Share(counts[target])), inset = (chunk - size) / 2;
                        frame.Marks.Add(new FindMark(x + inset, y + inset, size, target, Opacity(counts[target])));
                    }
                }
            }

            if (picked is not null)
            {
                var (x, y) = view.ToScreen(picked.X + 0.5, picked.Z + 0.5);
                frame.Ring = (x, y, Math.Max(11, zoom * 2));
            }
            return frame;
        }

        // How much of its chunk a target's square takes, and how solid it is: a single block is plain to see, and 30 or
        // so is as big and solid as it gets
        public static double Share(int count) => 0.35 + 0.35 * Fullness(count);
        public static double Opacity(int count) => 0.8 + 0.2 * Fullness(count);
        private static double Fullness(int count) => Math.Min(1, Math.Log2(count) / 5);
    }
}
