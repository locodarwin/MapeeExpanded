using NbtEditor;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using WorldEditor;

namespace Mapper
{
    // How a structure is known. Java saves every structure it generates with the chunk it starts in; Bedrock saves
    // only where some of them spawn their mobs (fortresses, witch huts, monuments, outposts) and its villages, and the
    // rest are recognised by blocks nothing else has
    public enum StructureSource { Saved, Blocks }

    // A structure and the box it fills. X, Y and Z are the middle of the box
    public record Structure(Dimension Dimension, StructureKind Kind, string Name, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ, StructureSource Source)
    {
        public int X => (int)Math.Floor((MinX + MaxX) / 2.0);
        public int Y => (int)Math.Floor((MinY + MaxY) / 2.0);
        public int Z => (int)Math.Floor((MinZ + MaxZ) / 2.0);

        public override string ToString() => $"{X}, {Y}, {Z}";
    }

    // One dimension's structures, as far as read. Java's are kept by the region they start in, and Bedrock's found by
    // their blocks by the region the blocks are in, so an update reads again only the regions the game has saved
    // since; Bedrock's saved records are few, and read whole each time
    public sealed class DimensionStructures
    {
        public Dimension Dimension { get; }

        // Sorted biggest kind first, then west to east
        public IReadOnlyList<Structure> All { get; }

        internal IReadOnlyDictionary<Coords, List<Structure>> ByRegion { get; }

        internal DimensionStructures(Dimension dimension, Dictionary<Coords, List<Structure>> byRegion, List<Structure>? records)
        {
            Dimension = dimension;
            ByRegion = byRegion;
            IEnumerable<Structure> pieces = byRegion.Values.SelectMany(s => s);
            List<Structure> all = records is null ? [.. pieces] : StructureMap.Merge(records.Concat(pieces));
            all.Sort((a, b) => a.Kind.Rank != b.Kind.Rank ? a.Kind.Rank.CompareTo(b.Kind.Rank) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            All = all;
        }
    }

    // Reading the structures of one dimension
    public static class StructureMap
    {
        // Structure pieces of one kind this close together are one structure: a stronghold's portal frame, the
        // trial spawners of one set of trial chambers, a fortress's spawn areas (its pieces stretch far, and the
        // game keeps fortresses hundreds of blocks apart), or villages the game keeps as several that overlap
        public const int Gap = 48;
        private static readonly Dictionary<string, int> Gaps = new()
        {
            ["fortress"] = 128,
            ["trial_chambers"] = 64,
            ["village"] = 0,
        };

        // Blocks that only one structure has, and the game can't give a player, for finding Bedrock's
        public static readonly IReadOnlyList<(string Kind, FindTarget Blocks)> Signatures =
        [
            ("stronghold", new FindTarget("Stronghold", ["minecraft:end_portal_frame"])),
            ("ancient_city", new FindTarget("Ancient city", ["minecraft:reinforced_deepslate"])),
            ("trial_chambers", new FindTarget("Trial chambers", ["minecraft:trial_spawner", "minecraft:vault"])),
        ];

        // Bedrock's spawn area kinds, in the chunk record tagged 57
        private const byte SpawnAreaTag = 57;
        private static readonly Dictionary<byte, string> SpawnAreaKinds = new()
        {
            [1] = "fortress",
            [2] = "swamp_hut",
            [3] = "monument",
            [5] = "pillager_outpost",
        };

        // Sorted biggest kind first, then west to east
        public static List<Structure> Read(SceneInfo scene, CancellationToken cancellation = default) => [.. Update(null, scene, null, cancellation).All];

        public static DimensionStructures None(Dimension dimension) => new(dimension, [], null);

        // earlier, read again in the regions given; everything when there's nothing earlier or no regions are given
        public static DimensionStructures Update(DimensionStructures? earlier, SceneInfo scene, IReadOnlyCollection<Coords>? regions, CancellationToken cancellation = default)
        {
            HashSet<Coords> exist = [.. scene.Regions];
            bool whole = earlier is null || regions is null;
            List<Coords> toRead = whole ? [.. exist] : [.. regions!.Where(exist.Contains)];

            Dictionary<Coords, List<Structure>> byRegion = whole ? [] : earlier!.ByRegion.Where(r => exist.Contains(r.Key) && !regions!.Contains(r.Key)).ToDictionary();
            List<Structure>? records = null;
            if (scene.RegionStore is BedrockRegionStore bedrock)
            {
                records = Records(scene.Dimension, bedrock, cancellation);
                if (scene.Dimension == Dimension.Overworld) FromBlocks(scene.Dimension, bedrock, toRead, byRegion, cancellation);
            }
            else
            {
                Java(scene, toRead, byRegion, cancellation);
            }
            return new DimensionStructures(scene.Dimension, byRegion, records);
        }

        private static void Java(SceneInfo scene, List<Coords> regions, Dictionary<Coords, List<Structure>> into, CancellationToken cancellation)
        {
            ConcurrentBag<(Coords Region, Structure Structure)> found = [];
            ChunkEnumerator enumerator = new(2, 8, () => scene.RegionStore, new ChunkEnumeratorFromRegionFactory(_ => new StartReader(scene.Dimension, found))) { Light = false };
            enumerator.Enumerate([.. regions], new Cancellable(cancellation));
            cancellation.ThrowIfCancellationRequested();

            foreach (Coords region in regions) into.Remove(region);
            foreach (var (region, structure) in found)
            {
                if (!into.TryGetValue(region, out List<Structure>? list)) into[region] = list = [];
                list.Add(structure);
            }
        }

        // The structures a Java chunk starts: structures.starts from 1.18, Level.Structures.Starts before. Each start
        // has its pieces' boxes; together they're the structure's
        public static IEnumerable<Structure> Starts(Dimension dimension, CompoundTag chunk)
        {
            CompoundTag root = chunk["Level"] is CompoundTag level ? level : chunk;
            if ((root["structures"] ?? root["Structures"]) is not CompoundTag structures) yield break;
            if ((structures["starts"] ?? structures["Starts"]) is not CompoundTag starts) yield break;

            foreach (var entry in starts)
            {
                if (entry.Value is not CompoundTag start) continue;
                string id = start["id"] is StringTag name ? name.InternalValue : entry.Key;
                if (StructureKinds.Of(id) is not var (kind, title)) continue;

                (int, int, int, int, int, int)? box = null;
                if ((start["Children"] ?? start["children"]) is ListTag children)
                {
                    foreach (Tag child in children)
                    {
                        if (child is CompoundTag piece && Box(piece["BB"]) is var pieceBox && pieceBox is not null) box = Union(box, pieceBox.Value);
                    }
                }
                box ??= Box(start["BB"]);
                if (box is not var (minX, minY, minZ, maxX, maxY, maxZ)) continue;

                yield return new Structure(dimension, kind, title, minX, minY, minZ, maxX, maxY, maxZ, StructureSource.Saved);
            }
        }

        // A box saved as six ints: the low corner, then the high one
        private static (int, int, int, int, int, int)? Box(Tag? tag)
        {
            if (tag is not ArrayTag { InternalArary: int[] values } || values.Length < 6) return null;
            return (Math.Min(values[0], values[3]), Math.Min(values[1], values[4]), Math.Min(values[2], values[5]),
                Math.Max(values[0], values[3]), Math.Max(values[1], values[4]), Math.Max(values[2], values[5]));
        }

        private static (int, int, int, int, int, int) Union((int, int, int, int, int, int)? a, (int, int, int, int, int, int) b)
        {
            if (a is not var (minX, minY, minZ, maxX, maxY, maxZ)) return b;
            return (Math.Min(minX, b.Item1), Math.Min(minY, b.Item2), Math.Min(minZ, b.Item3), Math.Max(maxX, b.Item4), Math.Max(maxY, b.Item5), Math.Max(maxZ, b.Item6));
        }

        // Where fortresses, witch huts, monuments and outposts spawn their mobs, and the villages, unjoined
        private static List<Structure> Records(Dimension dimension, BedrockRegionStore store, CancellationToken cancellation)
        {
            store.Ready.Wait(cancellation);
            if (store.World is not BedrockWorld world) return [];

            List<Structure> pieces = [];
            ConcurrentBag<byte[]> spawnKeys = [], villageKeys = [];
            world.Database.ForEachKey(key => IsSpawnAreaKey(key, store.Dimension) || IsVillageKey(key), key =>
            {
                if (IsVillageKey(key)) villageKeys.Add([.. key]);
                else spawnKeys.Add([.. key]);
            });
            cancellation.ThrowIfCancellationRequested();

            foreach (byte[] key in spawnKeys)
            {
                if (!world.Database.TryGet(key, out byte[]? value) || value is null) continue;
                foreach (var (kind, box) in SpawnAreas(value)) pieces.Add(Make(dimension, StructureKinds.Get(kind), box, StructureSource.Saved));
            }
            foreach (byte[] key in villageKeys)
            {
                if (VillageDimension(key) != store.Dimension || !world.Database.TryGet(key, out byte[]? value) || value is null) continue;
                if (Village(value) is (int, int, int, int, int, int) box) pieces.Add(Make(dimension, StructureKinds.Get("village"), box, StructureSource.Saved));
            }
            cancellation.ThrowIfCancellationRequested();
            return pieces;
        }

        private static bool IsSpawnAreaKey(ReadOnlySpan<byte> key, int dimension)
        {
            if (key.Length != (dimension == 0 ? 9 : 13) || key[^1] != SpawnAreaTag) return false;
            return dimension == 0 || BinaryPrimitives.ReadInt32LittleEndian(key[8..]) == dimension;
        }

        // A record of how many boxes, then each box as six ints and its kind
        public static List<(string Kind, (int, int, int, int, int, int) Box)> SpawnAreas(byte[] value)
        {
            List<(string, (int, int, int, int, int, int))> output = [];
            if (value.Length < 4) return output;
            int count = BinaryPrimitives.ReadInt32LittleEndian(value);
            for (int i = 0, at = 4; i < count && at + 25 <= value.Length; i++, at += 25)
            {
                int Int(int n) => BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(at + n * 4));
                if (!SpawnAreaKinds.TryGetValue(value[at + 24], out string? kind)) continue;
                output.Add((kind, (Math.Min(Int(0), Int(3)), Math.Min(Int(1), Int(4)), Math.Min(Int(2), Int(5)), Math.Max(Int(0), Int(3)), Math.Max(Int(1), Int(4)), Math.Max(Int(2), Int(5)))));
            }
            return output;
        }

        private static readonly byte[] VillagePrefix = Encoding.ASCII.GetBytes("VILLAGE_");
        private static readonly byte[] VillageInfo = Encoding.ASCII.GetBytes("_INFO");

        private static bool IsVillageKey(ReadOnlySpan<byte> key) => key.StartsWith(VillagePrefix) && key.EndsWith(VillageInfo);

        // VILLAGE_Overworld_<id>_INFO, VILLAGE_Nether_..., VILLAGE_TheEnd_...; before 1.16, VILLAGE_<id>_INFO, all in
        // the Overworld
        public static int VillageDimension(byte[] key)
        {
            string text = Encoding.ASCII.GetString(key, VillagePrefix.Length, key.Length - VillagePrefix.Length);
            if (text.StartsWith("Nether_")) return 1;
            if (text.StartsWith("TheEnd_")) return 2;
            return 0;
        }

        // A village's INFO record holds its bounds as X0, Y0, Z0 and X1, Y1, Z1
        public static (int, int, int, int, int, int)? Village(byte[] value)
        {
            try
            {
                if (Tag.FromNbtReader(new UnsafeNbtReader(value, 0) { IsFormatLittleEndian = true }) is not CompoundTag info) return null;
                int? Int(string name) => info[name] is Int32Tag tag ? tag.InternalValue : null;
                if (Int("X0") is not int x0 || Int("Y0") is not int y0 || Int("Z0") is not int z0 || Int("X1") is not int x1 || Int("Y1") is not int y1 || Int("Z1") is not int z1) return null;
                return (Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Each region's telltale blocks, joined into structures within the region; across regions they're joined
        // with the rest
        private static void FromBlocks(Dimension dimension, IRegionStore store, List<Coords> regions, Dictionary<Coords, List<Structure>> into, CancellationToken cancellation)
        {
            BlockQuery query = new([.. Signatures.Select(s => s.Blocks)]);
            new BlockFinder(query).Search(store, regions, (region, matches) =>
            {
                List<Structure> pieces = Merge(matches.Select(block =>
                    Make(dimension, StructureKinds.Get(Signatures[block.Target].Kind), (block.X, block.Y, block.Z, block.X, block.Y, block.Z), StructureSource.Blocks)));
                lock (into)
                {
                    if (pieces.Count > 0) into[region] = pieces;
                    else into.Remove(region);
                }
            }, cancellation);
        }

        private static Structure Make(Dimension dimension, StructureKind kind, (int, int, int, int, int, int) box, StructureSource source)
        {
            var (minX, minY, minZ, maxX, maxY, maxZ) = box;
            return new Structure(dimension, kind, kind.Name, minX, minY, minZ, maxX, maxY, maxZ, source);
        }

        // Joins pieces of one kind within Gap of each other, sideways, into one structure, until none are left that near
        public static List<Structure> Merge(IEnumerable<Structure> pieces)
        {
            List<Structure> output = [];
            foreach (var kind in pieces.GroupBy(p => p.Kind.Id))
            {
                int gap = Gaps.GetValueOrDefault(kind.Key, Gap);
                List<Structure> merged = [.. kind.DistinctBy(p => (p.MinX, p.MinY, p.MinZ, p.MaxX, p.MaxY, p.MaxZ))];
                bool joined = true;
                while (joined)
                {
                    joined = false;
                    for (int i = 0; i < merged.Count && !joined; i++)
                    {
                        for (int j = i + 1; j < merged.Count; j++)
                        {
                            Structure a = merged[i], b = merged[j];
                            if (a.MinX - gap > b.MaxX || b.MinX - gap > a.MaxX || a.MinZ - gap > b.MaxZ || b.MinZ - gap > a.MaxZ) continue;

                            merged[i] = a with
                            {
                                MinX = Math.Min(a.MinX, b.MinX), MinY = Math.Min(a.MinY, b.MinY), MinZ = Math.Min(a.MinZ, b.MinZ),
                                MaxX = Math.Max(a.MaxX, b.MaxX), MaxY = Math.Max(a.MaxY, b.MaxY), MaxZ = Math.Max(a.MaxZ, b.MaxZ)
                            };
                            merged.RemoveAt(j);
                            joined = true;
                            break;
                        }
                    }
                }
                output.AddRange(merged);
            }
            return output;
        }

        // Reads each Java chunk's structure starts and nothing else, with the region of the chunk they start in
        private sealed class StartReader(Dimension dimension, ConcurrentBag<(Coords, Structure)> found) : IObjectReader<ChunkParamater, IChunk?>
        {
            public IChunk? Read(ChunkParamater input)
            {
                CompoundTag root = input.Level["Level"] is CompoundTag level ? level : input.Level;
                if (root["xPos"] is not Int32Tag x || root["zPos"] is not Int32Tag z) return null;
                Coords region = new(x.InternalValue >> 5, z.InternalValue >> 5);
                foreach (Structure structure in Starts(dimension, input.Level)) found.Add((region, structure));
                return null;
            }
        }

        private sealed class Cancellable(CancellationToken cancellation) : IEnumerationBody
        {
            public void BeginCycle() => cancellation.ThrowIfCancellationRequested();
            public void EndCycle() { }
            public void BeginReadingRegion(int index, Coords region) { }
            public void EndReadingRegion(int index, Coords region) { }
            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk) { }
        }
    }
}
