using NbtEditor;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using WorldEditor;

namespace Mapper
{
    // A mob given a name, with a name tag or an anvil, where it was when the game last saved it. Known by the game's
    // own id for it, as it walks about. Tamed is a pet with an owner
    public record Pet(Dimension Dimension, string Id, string Type, string Name, double X, double Y, double Z, bool Tamed)
    {
        public int BlockX => (int)Math.Floor(X);
        public int BlockY => (int)Math.Floor(Y);
        public int BlockZ => (int)Math.Floor(Z);

        // "Wolf", "Iron golem"
        public string Kind => PetMap.KindOf(Type);

        public override string ToString() => $"{BlockX}, {BlockY}, {BlockZ}";
    }

    // One dimension's named mobs, as far as read, kept by the region they're saved in so an update reads again only
    // the regions the game has saved since
    public sealed class DimensionPets
    {
        public Dimension Dimension { get; }

        // By name, then kind
        public IReadOnlyList<Pet> All { get; }

        internal IReadOnlyDictionary<Coords, List<Pet>> ByRegion { get; }

        // The regions read the last time, which say which shown pets can be known to be gone
        public IReadOnlyCollection<Coords> Read { get; }

        public DimensionPets(Dimension dimension, Dictionary<Coords, List<Pet>> byRegion, IReadOnlyCollection<Coords> read)
        {
            Dimension = dimension;
            ByRegion = byRegion;
            Read = read;
            // A mob saved twice (the game moving it between chunks as it saved) is listed once
            All = [.. byRegion.Values.SelectMany(p => p).DistinctBy(p => p.Id)
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Kind, StringComparer.CurrentCultureIgnoreCase)];
        }

        public Pet? Find(string id) => All.FirstOrDefault(p => p.Id == id);

        // Whether a mob last seen at x, z was saved in a region read the last time
        public bool WasRead(double x, double z) => Read.Contains(new Coords((int)Math.Floor(x) >> 9, (int)Math.Floor(z) >> 9));
    }

    // Reading the named mobs of one dimension. Java keeps mobs in the entities folder beside region from 1.17, in each
    // chunk's Entities before. Bedrock keeps each mob under its own key from 1.18.30, listed by chunk under digp keys,
    // and in each chunk's entity record before
    public static class PetMap
    {
        public static List<Pet> Read(SceneInfo scene, CancellationToken cancellation = default) => [.. Update(null, scene, null, cancellation).All];

        public static DimensionPets None(Dimension dimension) => new(dimension, [], []);

        // Not mobs, though the game may save a name on them
        private static readonly HashSet<string> NotMobs =
        [
            "armor_stand", "item", "item_frame", "glow_item_frame", "painting", "leash_knot", "falling_block", "tnt",
            "experience_orb", "xp_orb", "marker", "interaction", "block_display", "item_display", "text_display",
            "area_effect_cloud", "end_crystal", "ender_crystal", "minecart", "chest_minecart", "furnace_minecart",
            "hopper_minecart", "tnt_minecart", "spawner_minecart", "command_block_minecart", "boat", "chest_boat"
        ];

        // earlier, read again in the regions given; everything when there's nothing earlier or no regions are given
        public static DimensionPets Update(DimensionPets? earlier, SceneInfo scene, IReadOnlyCollection<Coords>? regions, CancellationToken cancellation = default)
        {
            HashSet<Coords> exist = [.. scene.Regions];
            bool whole = earlier is null || regions is null;
            List<Coords> toRead = whole ? [.. exist] : [.. regions!.Where(exist.Contains)];

            Dictionary<Coords, List<Pet>> byRegion = whole ? [] : earlier!.ByRegion.Where(r => exist.Contains(r.Key) && !regions!.Contains(r.Key)).ToDictionary();
            foreach (Coords region in toRead) byRegion.Remove(region);

            ConcurrentBag<(Coords Region, Pet Pet)> found = [];
            if (scene.RegionStore is BedrockRegionStore bedrock) Bedrock(scene.Dimension, bedrock, [.. toRead], found, cancellation);
            else Java(scene, toRead, found, cancellation);

            foreach (var (region, pet) in found)
            {
                if (!byRegion.TryGetValue(region, out List<Pet>? list)) byRegion[region] = list = [];
                list.Add(pet);
            }
            return new DimensionPets(scene.Dimension, byRegion, toRead);
        }

        // "minecraft:iron_golem" to "Iron golem"
        public static string KindOf(string type)
        {
            string name = type.Contains(':') ? type[(type.IndexOf(':') + 1)..] : type;
            name = name.Replace('_', ' ').Trim();
            return name.Length == 0 ? "Mob" : char.ToUpperInvariant(name[0]) + name[1..];
        }

        private static void Java(SceneInfo scene, List<Coords> regions, ConcurrentBag<(Coords, Pet)> found, CancellationToken cancellation)
        {
            // From 1.17 the entities folder sits beside region, its files named the same; before, mobs are in the
            // chunks themselves
            string regionFolder = Path.Combine(scene.Level.Directory, scene.Dimension.GetRegionFolder(scene.Level.Version.Version));
            string entities = Path.Combine(Path.GetDirectoryName(regionFolder) ?? scene.Level.Directory, "entities");
            IRegionStore store = scene.RegionStore;
            if (Directory.Exists(entities))
            {
                AnvilRegionStore separate = new();
                separate.Itemize(entities);
                store = separate;
            }
            List<Coords> present = [.. regions.Where(store.Exists)];

            ChunkEnumerator enumerator = new(2, 8, () => store, new ChunkEnumeratorFromRegionFactory(_ => new PetReader(scene.Dimension, found))) { Light = false };
            enumerator.Enumerate([.. present], new Cancellable(cancellation));
            cancellation.ThrowIfCancellationRequested();
        }

        // The named mobs in a Java chunk, or an entities file's chunk, and those riding them
        public static IEnumerable<Pet> InChunk(Dimension dimension, CompoundTag chunk)
        {
            CompoundTag root = chunk["Level"] is CompoundTag level ? level : chunk;
            if (root["Entities"] is not ListTag entities) yield break;
            List<Pet> output = [];
            foreach (Tag tag in entities)
            {
                if (tag is CompoundTag entity) FromEntity(dimension, entity, output);
            }
            foreach (Pet pet in output) yield return pet;
        }

        // A mob as either edition saves it, if it has a name, and any riding it (Java keeps them inside it). Java's
        // id is "id", its name JSON text (NBT text from 1.21.5); Bedrock's "identifier", its name plain text
        public static void FromEntity(Dimension dimension, CompoundTag entity, List<Pet> into)
        {
            if (entity["Passengers"] is ListTag riders)
            {
                foreach (Tag rider in riders)
                {
                    if (rider is CompoundTag riding) FromEntity(dimension, riding, into);
                }
            }

            bool bedrock = entity["identifier"] is StringTag;
            if ((entity["id"] ?? entity["identifier"]) is not StringTag type) return;
            string name = entity["CustomName"] switch
            {
                StringTag text when bedrock => SignText.StripCodes(text.InternalValue),
                Tag text => SignText.Plain(text),
                null => string.Empty
            };
            name = name.Trim();
            if (name.Length == 0) return;

            string id = type.InternalValue;
            string bare = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
            if (NotMobs.Contains(bare.ToLowerInvariant()) || bare.EndsWith("boat", StringComparison.OrdinalIgnoreCase) || bare.EndsWith("_raft", StringComparison.OrdinalIgnoreCase)) return;
            if (!id.Contains(':')) id = "minecraft:" + ToSnake(bare);

            if (entity["Pos"] is not ListTag position || position.Count < 3) return;
            double? x = Number(position[0]), y = Number(position[1]), z = Number(position[2]);
            if (x is null || y is null || z is null) return;

            into.Add(new Pet(dimension, IdOf(entity, id, name), id.ToLowerInvariant(), name, x.Value, y.Value, z.Value, IsTamed(entity)));
        }

        // Before 1.11 Java's ids were "Wolf", "VillagerGolem"; near enough as they are
        private static string ToSnake(string name)
        {
            StringBuilder output = new();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) output.Append('_');
                output.Append(char.ToLowerInvariant(name[i]));
            }
            return output.ToString();
        }

        private static double? Number(Tag tag) => tag switch
        {
            DoubleTag value => value.InternalValue,
            SingleTag value => value.InternalValue,
            _ => null
        };

        // The game's id for the mob: Java's UUID (four ints from 1.16, two longs before), Bedrock's UniqueID; failing
        // those, its kind and name
        private static string IdOf(CompoundTag entity, string type, string name)
        {
            // Read from a pool, an array may be longer than what was saved in it
            if (entity["UUID"] is ArrayTag { InternalArary: int[] parts } && parts.Length >= 4)
                return string.Concat(parts.Take(4).Select(p => ((uint)p).ToString("x8")));
            if (entity["UUIDMost"] is Int64Tag most && entity["UUIDLeast"] is Int64Tag least)
                return $"{(ulong)most.InternalValue:x16}{(ulong)least.InternalValue:x16}";
            if (entity["UniqueID"] is Int64Tag unique) return unique.InternalValue.ToString("x16");
            return $"{type}|{name}";
        }

        // Java: an Owner (UUID from 1.16), OwnerUUID before, or Tame for horses. Bedrock: OwnerNew, -1 for none
        private static bool IsTamed(CompoundTag entity)
        {
            if (entity["Owner"] is ArrayTag or StringTag) return true;
            if (entity["OwnerUUID"] is StringTag owner && owner.InternalValue.Length > 0) return true;
            if (entity["Tame"] is SignedByteTag tame && tame.InternalValue != 0) return true;
            if (entity["IsTamed"] is SignedByteTag tamed && tamed.InternalValue != 0) return true;
            return entity["OwnerNew"] is Int64Tag ownerNew && ownerNew.InternalValue != -1;
        }

        private static readonly byte[] Digp = "digp"u8.ToArray();
        private static readonly byte[] ActorPrefix = "actorprefix"u8.ToArray();
        private static readonly byte[] CustomName = "CustomName"u8.ToArray();

        private static void Bedrock(Dimension dimension, BedrockRegionStore store, HashSet<Coords> regions, ConcurrentBag<(Coords, Pet)> found, CancellationToken cancellation)
        {
            store.Ready.Wait(cancellation);
            if (store.World is not BedrockWorld world || regions.Count == 0) return;

            // From 1.18.30: digp + chunk x, z (+ dimension unless the Overworld) lists the chunk's mobs by id
            ConcurrentBag<(byte[] Key, Coords Region)> lists = [];
            ConcurrentBag<byte[]> records = [];
            world.Database.ForEachKey(key =>
            {
                if (DigpChunk(key, store.Dimension) is (int x, int z)) return regions.Contains(new Coords(x >> 5, z >> 5));
                return BedrockKey.TryParse(key, out BedrockKey parsed) && parsed.Tag == BedrockChunkTag.Entity
                    && parsed.Dimension == store.Dimension && regions.Contains(new Coords(parsed.X >> 5, parsed.Z >> 5));
            }, key =>
            {
                if (DigpChunk(key, store.Dimension) is (int x, int z)) lists.Add(([.. key], new Coords(x >> 5, z >> 5)));
                else records.Add([.. key]);
            });
            cancellation.ThrowIfCancellationRequested();

            foreach (var (key, region) in lists)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!world.Database.TryGet(key, out byte[]? ids) || ids is null) continue;
                for (int i = 0; i + 8 <= ids.Length; i += 8)
                {
                    byte[] actor = [.. ActorPrefix, .. ids.AsSpan(i, 8)];
                    if (!world.Database.TryGet(actor, out byte[]? value) || value is null || value.AsSpan().IndexOf(CustomName) < 0) continue;
                    foreach (Pet pet in InRecord(dimension, value)) found.Add((region, pet));
                }
            }

            // Before 1.18.30: each chunk's mobs together in its entity record
            foreach (byte[] key in records)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!BedrockKey.TryParse(key, out BedrockKey parsed) || !world.Database.TryGet(key, out byte[]? value) || value is null) continue;
                if (value.AsSpan().IndexOf(CustomName) < 0) continue;
                Coords region = new(parsed.X >> 5, parsed.Z >> 5);
                foreach (Pet pet in InRecord(dimension, value)) found.Add((region, pet));
            }
        }

        // The chunk a digp key lists the mobs of, if it's one, in the dimension given
        public static (int X, int Z)? DigpChunk(ReadOnlySpan<byte> key, int dimension)
        {
            if ((key.Length != 12 && key.Length != 16) || !key.StartsWith(Digp)) return null;
            int keyDimension = key.Length == 16 ? BinaryPrimitives.ReadInt32LittleEndian(key[12..]) : 0;
            if (keyDimension != dimension) return null;
            return (BinaryPrimitives.ReadInt32LittleEndian(key[4..]), BinaryPrimitives.ReadInt32LittleEndian(key[8..]));
        }

        // A Bedrock mob, or an old chunk's entity record of them: one little-endian compound after another
        public static List<Pet> InRecord(Dimension dimension, byte[] value)
        {
            List<Pet> output = [];
            FastBufferProvider buffer = new(value);
            UnsafeNbtReader reader = new(buffer, ArrayPoolAllocation.CreateDefault()) { IsFormatLittleEndian = true };
            try
            {
                while (buffer.Index < value.Length)
                {
                    if (Tag.FromNbtReader(reader) is CompoundTag entity) FromEntity(dimension, entity, output);
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException or NotSupportedException)
            {
                // A damaged record keeps what was read before the damage
            }
            return output;
        }

        // Reads each Java chunk's named mobs and nothing else, with the region of the chunk: Position in an entities
        // file, xPos and zPos in a chunk
        private sealed class PetReader(Dimension dimension, ConcurrentBag<(Coords, Pet)> found) : IObjectReader<ChunkParamater, IChunk?>
        {
            public IChunk? Read(ChunkParamater input)
            {
                CompoundTag root = input.Level["Level"] is CompoundTag level ? level : input.Level;
                Coords region;
                if (root["Position"] is ArrayTag { InternalArary: int[] position } && position.Length >= 2) region = new(position[0] >> 5, position[1] >> 5);
                else if (root["xPos"] is Int32Tag x && root["zPos"] is Int32Tag z) region = new(x.InternalValue >> 5, z.InternalValue >> 5);
                else return null;
                foreach (Pet pet in InChunk(dimension, input.Level)) found.Add((region, pet));
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

    public static class PetText
    {
        // Whether a mob's name and kind together have every word, in any case
        public static bool Says(Pet pet, IReadOnlyCollection<string> words)
        {
            if (words.Count == 0) return true;
            string text = $"{pet.Name} {pet.Kind}";
            return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
