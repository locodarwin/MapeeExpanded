using WorldEditor;

namespace Mapper
{
    // Matches next to each other (sides, edges or corners touching) that are the same target, such as a vein of ore.
    // X, Y and Z are the member nearest their middle, so the place given is one of the blocks
    public record FoundCluster(int Target, int Count, int X, int Y, int Z, int MinY, int MaxY);

    // A search's matches, kept by region so a region searched again (when the game saves it) replaces its own. Safe to
    // read from the drawing thread while a search adds to it
    public class FindResults
    {
        public BlockQuery Query { get; }

        private readonly object _gate = new();
        private readonly Dictionary<Coords, BlockMatch[]> _regions = [];
        private int _total;
        private List<FoundCluster>? _clusters;
        private Dictionary<Coords, int[]>? _chunks;
        private int[]? _counts;

        public FindResults(BlockQuery query)
        {
            Query = query;
        }

        public int Total
        {
            get { lock (_gate) return _total; }
        }

        // Regions searched so far, whether they held anything or not
        public int Regions
        {
            get { lock (_gate) return _regions.Count; }
        }

        public void Set(Coords region, IReadOnlyList<BlockMatch> matches)
        {
            lock (_gate)
            {
                if (_regions.TryGetValue(region, out BlockMatch[]? old)) _total -= old.Length;
                _regions[region] = [.. matches];
                _total += matches.Count;
                _clusters = null;
                _chunks = null;
                _counts = null;
            }
        }

        // How many of each target were found
        public IReadOnlyList<int> Counts
        {
            get
            {
                lock (_gate)
                {
                    if (_counts is not null) return _counts;
                    int[] counts = new int[Query.Targets.Count];
                    foreach (BlockMatch[] matches in _regions.Values)
                    {
                        foreach (BlockMatch match in matches) counts[match.Target]++;
                    }
                    return _counts = counts;
                }
            }
        }

        public IReadOnlyList<FoundCluster> Clusters
        {
            get
            {
                lock (_gate) return _clusters ??= Cluster(_regions.Values.SelectMany(m => m));
            }
        }

        // What each chunk holds: how many of each target
        public IReadOnlyDictionary<Coords, int[]> Chunks
        {
            get
            {
                lock (_gate) return _chunks ??= CountChunks(_regions.Values.SelectMany(m => m));
            }
        }

        public List<BlockMatch> All()
        {
            lock (_gate) return [.. _regions.Values.SelectMany(m => m)];
        }

        // The matches in an area of blocks (corners included)
        public List<BlockMatch> MatchesIn(int left, int top, int right, int bottom)
        {
            List<BlockMatch> output = [];
            lock (_gate)
            {
                for (int rz = MathUtilities.FindSectionY(top, 512); rz <= MathUtilities.FindSectionY(bottom, 512); rz++)
                {
                    for (int rx = MathUtilities.FindSectionY(left, 512); rx <= MathUtilities.FindSectionY(right, 512); rx++)
                    {
                        if (!_regions.TryGetValue(new Coords(rx, rz), out BlockMatch[]? matches)) continue;
                        foreach (BlockMatch match in matches)
                        {
                            if (match.X >= left && match.X <= right && match.Z >= top && match.Z <= bottom) output.Add(match);
                        }
                    }
                }
            }
            return output;
        }

        // Clusters nearest a point first (by the horizontal distance; height is left to the list's Y)
        public static List<FoundCluster> ByDistance(IEnumerable<FoundCluster> clusters, double x, double z)
        {
            return [.. clusters.OrderBy(c => Distance(c, x, z)).ThenBy(c => c.Y)];
        }

        public static double Distance(FoundCluster cluster, double x, double z)
        {
            double dx = cluster.X + 0.5 - x, dz = cluster.Z + 0.5 - z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public static List<FoundCluster> Cluster(IEnumerable<BlockMatch> matches)
        {
            BlockMatch[] all = [.. matches];
            Dictionary<(int, int, int, int), int> at = new(all.Length);
            for (int i = 0; i < all.Length; i++) at.TryAdd((all[i].X, all[i].Y, all[i].Z, all[i].Target), i);

            int[] parent = new int[all.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Root(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }

            for (int i = 0; i < all.Length; i++)
            {
                BlockMatch m = all[i];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (!at.TryGetValue((m.X + dx, m.Y + dy, m.Z + dz, m.Target), out int j) || j == i) continue;
                            int a = Root(i), b = Root(j);
                            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                        }
                    }
                }
            }

            List<FoundCluster> output = [];
            foreach (IGrouping<int, int> group in Enumerable.Range(0, all.Length).GroupBy(Root))
            {
                BlockMatch[] members = [.. group.Select(i => all[i])];
                double mx = members.Average(m => m.X), my = members.Average(m => m.Y), mz = members.Average(m => m.Z);
                BlockMatch middle = members.MinBy(m => (m.X - mx) * (m.X - mx) + (m.Y - my) * (m.Y - my) + (m.Z - mz) * (m.Z - mz));
                output.Add(new FoundCluster(middle.Target, members.Length, middle.X, middle.Y, middle.Z, members.Min(m => m.Y), members.Max(m => m.Y)));
            }
            return output;
        }

        private Dictionary<Coords, int[]> CountChunks(IEnumerable<BlockMatch> matches)
        {
            Dictionary<Coords, int[]> counts = [];
            foreach (BlockMatch match in matches)
            {
                Coords chunk = new(MathUtilities.FindSectionY(match.X, 16), MathUtilities.FindSectionY(match.Z, 16));
                if (!counts.TryGetValue(chunk, out int[]? perTarget)) counts[chunk] = perTarget = new int[Query.Targets.Count];
                perTarget[match.Target]++;
            }
            return counts;
        }
    }
}
