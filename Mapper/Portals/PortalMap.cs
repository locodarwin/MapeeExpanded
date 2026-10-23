using WorldEditor;

namespace Mapper
{
    // A Nether portal: the purple blocks inside one frame. X and Z are its middle block, MinY where you stand in it
    public record Portal(Dimension Dimension, int X, int MinY, int MaxY, int Z, int Blocks)
    {
        public override string ToString() => $"{X}, {MinY}, {Z}";
    }

    // Where stepping into a portal takes you. Target is the spot the game looks around, the portal's position
    // divided or multiplied by 8; To is the portal it finds there, or null when it would build a new one; Back is
    // where To leads in turn
    public record PortalLink(Portal From, int TargetX, int TargetY, int TargetZ, Portal? To, Portal? Back)
    {
        // A portal whose partner leads somewhere else: going through and straight back lands somewhere new
        public bool Mismatched => To is not null && Back != From;
    }

    // The Nether portals of a world's Overworld and Nether, and how they link up. The game, Java and Bedrock alike,
    // takes the portal nearest the target within a square around it: 128 blocks each way in the Overworld, 16 in the
    // Nether, any height; the nearest by straight-line distance, and of two as near, the lower
    public class PortalMap
    {
        public const int OverworldRadius = 128;
        public const int NetherRadius = 16;

        public static readonly FindTarget Blocks = new("Nether portals", ["minecraft:nether_portal"]);

        public IReadOnlyList<Portal> Overworld { get; }
        public IReadOnlyList<Portal> Nether { get; }

        private readonly Dictionary<Portal, PortalLink> _links = [];

        public static PortalMap Empty { get; } = new([], []);

        public IEnumerable<Portal> In(Dimension dimension) => dimension == Dimension.Nether ? Nether : dimension == Dimension.Overworld ? Overworld : [];

        public PortalLink Link(Portal portal) => _links[portal];

        public PortalMap(IEnumerable<BlockMatch> overworld, IEnumerable<BlockMatch> nether)
        {
            var (overworldPortals, overworldBlocks) = Group(Dimension.Overworld, overworld);
            var (netherPortals, netherBlocks) = Group(Dimension.Nether, nether);
            Overworld = overworldPortals;
            Nether = netherPortals;

            Dictionary<Portal, (int X, int Y, int Z, Portal? To)> leads = [];
            foreach (Portal portal in Overworld) leads[portal] = Lead(portal, netherBlocks, NetherRadius);
            foreach (Portal portal in Nether) leads[portal] = Lead(portal, overworldBlocks, OverworldRadius);

            foreach (var (portal, (x, y, z, to)) in leads)
            {
                _links[portal] = new PortalLink(portal, x, y, z, to, to is null ? null : leads[to].To);
            }
        }

        // Searches both dimensions for portal blocks. A dimension the world doesn't have is left empty
        public static PortalMap Read(SceneInfo? overworld, SceneInfo? nether, CancellationToken cancellation = default)
        {
            return new PortalMap(Search(overworld, cancellation), Search(nether, cancellation));
        }

        public static List<BlockMatch> Search(SceneInfo? scene, CancellationToken cancellation = default)
        {
            if (scene is not SceneInfo info) return [];
            BlockQuery query = new([Blocks]);
            FindResults results = new(query);
            new BlockFinder(query).Search(info.RegionStore, [.. info.Regions], results.Set, cancellation);
            return results.All();
        }

        // Where a point in one dimension is in the other: divided by 8 going to the Nether, multiplied coming back
        public static (int X, int Z) Across(Dimension from, double x, double z)
        {
            double scale = from == Dimension.Nether ? 8 : 1.0 / 8;
            return ((int)Math.Floor(x * scale), (int)Math.Floor(z * scale));
        }

        private static (List<Portal>, List<(BlockMatch Block, Portal Portal)>) Group(Dimension dimension, IEnumerable<BlockMatch> blocks)
        {
            List<Portal> portals = [];
            List<(BlockMatch, Portal)> members = [];
            foreach (var (cluster, blocksIn) in FindResults.Groups(blocks).OrderBy(g => g.Cluster.X).ThenBy(g => g.Cluster.Z).ThenBy(g => g.Cluster.MinY))
            {
                Portal portal = new(dimension, cluster.X, cluster.MinY, cluster.MaxY, cluster.Z, cluster.Count);
                portals.Add(portal);
                foreach (BlockMatch block in blocksIn) members.Add((block, portal));
            }
            return (portals, members);
        }

        // Stepped into in its middle, at its foot
        private static (int, int, int, Portal?) Lead(Portal portal, List<(BlockMatch Block, Portal Portal)> other, int radius)
        {
            var (x, z) = Across(portal.Dimension, portal.X + 0.5, portal.Z + 0.5);
            int y = portal.MinY;

            Portal? nearest = null;
            long best = long.MaxValue;
            int bestY = int.MaxValue;
            foreach (var (block, candidate) in other)
            {
                long dx = block.X - x, dz = block.Z - z, dy = block.Y - y;
                if (Math.Abs(dx) > radius || Math.Abs(dz) > radius) continue;

                long distance = dx * dx + dy * dy + dz * dz;
                if (distance < best || (distance == best && block.Y < bestY))
                {
                    (best, bestY, nearest) = (distance, block.Y, candidate);
                }
            }
            return (x, y, z, nearest);
        }
    }
}
