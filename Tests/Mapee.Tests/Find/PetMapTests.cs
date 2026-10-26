using Mapee.Tests.Fixtures;
using Mapper;
using NbtEditor;
using System.Buffers.Binary;
using WorldEditor;

namespace Mapee.Tests.Find
{
    // Named mobs: what each edition and version saves for them, reading a world's, and the ones picked to show: kept
    // with the markup, moved to where they're saved next, and gone when they're gone
    public class PetMapTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-pets-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        // A block a screen unit, with block 0, 0 at the top left
        private sealed class BlockAPixel : IMarkupView
        {
            public string Dimension => WorldEditor.Dimension.Overworld.Namespace;
            public (double X, double Y) ToScreen(double x, double z) => (x, z);
            public (double X, double Z) ToWorld(double x, double y) => (x, y);
        }

        private static CompoundTag Read(Dictionary<string, object> values) =>
            (CompoundTag)Tag.FromNbtReader(new UnsafeNbtReader(Nbt.Compound(values, false), 0) { IsFormatLittleEndian = false });

        private static Dictionary<string, object> Mob(string id, double x, object? name, Dictionary<string, object>? more = null)
        {
            Dictionary<string, object> output = new() { ["id"] = id, ["Pos"] = new List<object> { x, 64.0, 2.5 }, ["Health"] = 20f };
            if (name is not null) output["CustomName"] = name;
            foreach (var (key, value) in more ?? []) output[key] = value;
            return output;
        }

        [Fact]
        public void ReadsJavaMobsOfEveryVersion()
        {
            // An entities file's chunk, from 1.17: JSON names until 1.21.5, NBT text after; riders inside their mount
            CompoundTag chunk = Read(new()
            {
                ["Position"] = new[] { 0, 0 },
                ["Entities"] = new List<object>
                {
                    Mob("minecraft:wolf", 1.5, "{\"text\":\"Rex\"}", new() { ["UUID"] = new[] { 1, 2, 3, 4 }, ["Owner"] = new[] { 9, 9, 9, 9 } }),
                    Mob("minecraft:cat", 2.5, new Dictionary<string, object> { ["text"] = "Tom", ["color"] = "gold" }, new() { ["UUID"] = new[] { 5, 6, 7, 8 } }),
                    Mob("minecraft:cow", 3.5, null),
                    Mob("minecraft:armor_stand", 4.5, "\"Scarecrow\""),
                    Mob("minecraft:oak_boat", 5.5, "\"Dinghy\"", new() { ["Passengers"] = new List<object> { Mob("minecraft:horse", 5.5, "\"Rider\"", new() { ["Tame"] = (byte)1 }) } })
                }
            });
            List<Pet> pets = [.. PetMap.InChunk(Dimension.Overworld, chunk)];

            Assert.Equal(["Rex", "Tom", "Rider"], pets.Select(p => p.Name));
            Assert.Equal(["Wolf", "Cat", "Horse"], pets.Select(p => p.Kind));
            Assert.Equal([true, false, true], pets.Select(p => p.Tamed));
            Assert.Equal("00000001000000020000000300000004", pets[0].Id);
            Assert.Equal((1, 64, 2), (pets[0].BlockX, pets[0].BlockY, pets[0].BlockZ));

            // Before 1.17, in the chunk itself; before 1.16 two longs for the id and OwnerUUID; before 1.11 "Wolf"
            CompoundTag old = Read(new()
            {
                ["Level"] = new Dictionary<string, object>
                {
                    ["xPos"] = 0,
                    ["zPos"] = 0,
                    ["Entities"] = new List<object>
                    {
                        Mob("Wolf", -0.5, "Old Yeller", new() { ["UUIDMost"] = 1L, ["UUIDLeast"] = 2L, ["OwnerUUID"] = "someone" }),
                        Mob("VillagerGolem", 8, "Clank")
                    }
                }
            });
            List<Pet> older = [.. PetMap.InChunk(Dimension.Overworld, old)];
            Assert.Equal(["Old Yeller", "Clank"], older.Select(p => p.Name));
            Assert.Equal(("minecraft:wolf", -1, true), (older[0].Type, older[0].BlockX, older[0].Tamed));
            Assert.Equal("00000000000000010000000000000002", older[0].Id);
            Assert.Equal("Villager golem", older[1].Kind);
        }

        private static byte[] BedrockMob(string id, float x, string? name, long unique, long owner = -1) =>
            Nbt.Compound(new()
            {
                ["identifier"] = id,
                ["Pos"] = new List<object> { x, 70f, -3.5f },
                ["UniqueID"] = unique,
                ["OwnerNew"] = owner,
                ["CustomName"] = name ?? string.Empty
            });

        [Fact]
        public void ReadsBedrockMobsAndTheirNamesAsTheyAre()
        {
            byte[] record = [.. BedrockMob("minecraft:wolf", 1.5f, "§6[Ghost]", 77, owner: 5), .. BedrockMob("minecraft:sheep", 2, null, 78), .. BedrockMob("minecraft:horse", 3, "Edgy", 79)];
            List<Pet> pets = PetMap.InRecord(Dimension.Overworld, record);

            // A Bedrock name is plain text, brackets and all
            Assert.Equal(["[Ghost]", "Edgy"], pets.Select(p => p.Name));
            Assert.Equal([true, false], pets.Select(p => p.Tamed));
            Assert.Equal(77L.ToString("x16"), pets[0].Id);
            Assert.Equal((1, 70, -4), (pets[0].BlockX, pets[0].BlockY, pets[0].BlockZ));

            // A record cut short keeps the mobs before the cut
            Assert.Single(PetMap.InRecord(Dimension.Overworld, record[..^20]));
        }

        [Fact]
        public void KnowsADigpKeyAndItsDimension()
        {
            byte[] key = [.. "digp"u8, 0, 0, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(4), -3);
            BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(8), 40);
            Assert.Equal((-3, 40), PetMap.DigpChunk(key, 0));
            Assert.Null(PetMap.DigpChunk(key, 1));

            byte[] nether = [.. key, 1, 0, 0, 0];
            Assert.Equal((-3, 40), PetMap.DigpChunk(nether, 1));
            Assert.Null(PetMap.DigpChunk(nether, 0));
            Assert.Null(PetMap.DigpChunk("digpXXXX"u8, 0));
        }

        [Fact]
        public void ReadsAJavaWorldsEntitiesFolder()
        {
            using JavaTestWorld world = SmokeWorld.Write();
            SceneInfo scene = new(WorldLoader.ReadLevel(world.LevelDat), Dimension.Overworld);
            DimensionPets read = PetMap.Update(null, scene, null);

            Pet pet = Assert.Single(read.All);
            Assert.Equal((SmokeWorld.PetName, "Wolf", true), (pet.Name, pet.Kind, pet.Tamed));
            Assert.Equal((-13, 65, 18), (pet.BlockX, pet.BlockY, pet.BlockZ));
            Assert.True(read.WasRead(pet.X, pet.Z));

            // Its region alone read again finds it again; another region's, not
            Assert.Same(pet, Assert.Single(PetMap.Update(read, scene, [new Coords(0, 0)]).All));
            Assert.NotSame(pet, Assert.Single(PetMap.Update(read, scene, [new Coords(-1, 0)]).All));
        }

        [Fact]
        public void ReadsABedrockWorldFromEitherKindOfKey()
        {
            static byte[] Digp(int x, int z, params long[] ids)
            {
                byte[] key = [.. "digp"u8, 0, 0, 0, 0, 0, 0, 0, 0];
                BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(4), x);
                BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(8), z);
                return key;
            }
            static byte[] Ids(params long[] ids)
            {
                byte[] value = new byte[ids.Length * 8];
                for (int i = 0; i < ids.Length; i++) BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(i * 8), ids[i]);
                return value;
            }
            static byte[] Actor(long id)
            {
                byte[] key = [.. "actorprefix"u8, 0, 0, 0, 0, 0, 0, 0, 0];
                BinaryPrimitives.WriteInt64LittleEndian(key.AsSpan(11), id);
                return key;
            }

            using BedrockTestWorld world = new BedrockTestWorld()
                .Chunk(0, 0)
                .Chunk(40, 0)
                // From 1.18.30: each mob under its own key, listed by chunk
                .Put(Digp(0, 0), Ids(11, 12))
                .Put(Actor(11), BedrockMob("minecraft:wolf", 3, "Maggie", 11, owner: 1))
                .Put(Actor(12), BedrockMob("minecraft:pig", 4, null, 12))
                // Before: the chunk's mobs together
                .Put(BedrockTestWorld.Key(40, 0, 0, BedrockChunkTag.Entity), BedrockMob("minecraft:parrot", 645, "Mr. Blueberry", 13, owner: 1))
                .Save("Bedrock pet test");
            SceneInfo scene = new(WorldLoader.ReadLevel(world.LevelDat), Dimension.Overworld);

            DimensionPets read = PetMap.Update(null, scene, null);
            Assert.Equal(["Maggie", "Mr. Blueberry"], read.All.Select(p => p.Name));
            Assert.Equal(["Wolf", "Parrot"], read.All.Select(p => p.Kind));

            // The far region alone read again: the near one's mob is kept as it was
            DimensionPets again = PetMap.Update(read, scene, [new Coords(1, 0)]);
            Assert.Same(read.Find(11L.ToString("x16")), again.Find(11L.ToString("x16")));
        }

        private static Pet Pet(string id, string name, double x, Dimension? dimension = null) =>
            new(dimension ?? Dimension.Overworld, id, "minecraft:wolf", name, x, 64, 0.5, true);

        private static DimensionPets ReadAs(Dimension dimension, params Pet[] pets) =>
            new(dimension, pets.GroupBy(p => new Coords((int)Math.Floor(p.X) >> 9, (int)Math.Floor(p.Z) >> 9)).ToDictionary(g => g.Key, g => g.ToList()), [new Coords(0, 0)]);

        [Fact]
        public void ShownPetsAreKeptWithTheMarkupAndFollowTheirMobs()
        {
            MarkupEditor editor = new(new BlockAPixel());
            int edits = 0;
            editor.Edited += (sender, e) => edits++;

            Assert.Equal(2, editor.ShowPets([Pet("a", "Rex", 1.5), Pet("b", "Tom", 2.5), Pet("a", "Rex", 1.5)]));
            Assert.Equal(0, editor.ShowPets([Pet("b", "Tom", 9.5)]));
            Assert.Equal(1, edits);

            // Not undone with the pins and zones
            editor.Import(new MarkupDocument() { Pins = [new MapPin() { X = 5, Z = 5 }] });
            Assert.True(editor.Undo());
            Assert.Equal(2, editor.Document.Pets.Count);

            // Read again: one has walked off, one is gone, one last seen in a region not read again is left alone
            editor.ShowPets([Pet("c", "Far", 700.5)]);
            Assert.True(editor.UpdatePets(ReadAs(Dimension.Overworld, Pet("a", "Rex", 30.5))));
            Assert.Equal(["Rex", "Far"], editor.Document.Pets.Select(p => p.Name));
            Assert.Equal(30, editor.Document.Pets[0].X);
            Assert.False(editor.UpdatePets(ReadAs(Dimension.Overworld, Pet("a", "Rex", 30.5))));

            // Through a portal: found in the Nether, it's shown there
            Assert.True(editor.UpdatePets(ReadAs(Dimension.Nether, Pet("a", "Rex", 3.5, Dimension.Nether))));
            Assert.Equal(Dimension.Nether.Namespace, editor.Document.PetById("a")!.Dimension);

            Assert.Equal(1, editor.HidePets([editor.Document.Pets[1]]));
            Assert.Equal("Rex", Assert.Single(editor.Document.Pets).Name);
        }

        [Fact]
        public void ShownPetsAreSavedAndReadBack()
        {
            MarkupStore store = new(_folder);
            string world = Path.Combine(_folder, "worlds", "Pets");
            MarkupDocument document = store.Load(world);
            document.Pets.Add(MapPet.Of(new Pet(Dimension.Nether, "id1", "minecraft:strider", "Lava Larry", 1.5, 2, 3.5, false)));
            store.Save(document);

            MapPet pet = Assert.Single(store.Load(world).Pets);
            Assert.Equal((Dimension.Nether.Namespace, "id1", "Lava Larry", 1, 2, 3), (pet.Dimension, pet.Id, pet.Name, pet.X, pet.Y, pet.Z));
            Assert.Equal("Strider", pet.Description);

            // Markup from before pets has none; one without an id is dropped, one shown twice kept once
            string file = Path.Combine(_folder, "old.json");
            File.WriteAllText(file, """{ "version": 1, "pins": [], "zones": [] }""");
            Assert.Empty(MarkupStore.Read(file).Pets);
            File.WriteAllText(file, """{ "version": 1, "pets": [ { "id": "x", "name": "Rex", "type": "minecraft:wolf", "tamed": true }, { "id": "x" }, { "name": "No id" } ] }""");
            MapPet read = Assert.Single(MarkupStore.Read(file).Pets);
            Assert.Equal(("minecraft:overworld", "Tamed wolf"), (read.Dimension, read.Description));

            // Brought in from another file only where it isn't shown already
            MarkupDocument mine = new() { Pets = [read] };
            Assert.Equal(1, mine.MergePets(new MarkupDocument() { Pets = [read.Clone(), MapPet.Of(Pet("y", "New", 1))] }));
        }

        [Fact]
        public void PetsAreDrawnHoveredAndNotOpenedLikePins()
        {
            MarkupEditor editor = new(new BlockAPixel());
            editor.ShowPets([Pet("a", "Rex", 100.5)]);
            List<MarkupItem?> asked = [];
            editor.OpenRequested += (sender, item) => asked.Add(item);

            MarkupFrame frame = MarkupFrame.Build(editor.Document, editor.View, 400, 400, editor);
            PetShape shape = Assert.Single(frame.Pets);
            Assert.True(shape.Labelled);
            Assert.False(shape.Hovered);

            editor.PointerMove(101, 1, false);
            Assert.Same(editor.Document.Pets[0], editor.HoveredPet);
            shape = Assert.Single(MarkupFrame.Build(editor.Document, editor.View, 400, 400, editor).Pets);
            Assert.True(shape.Hovered);
            Assert.Equal(["Rex", "Tamed wolf", "100, 64, 0"], shape.Lines);

            // A click on it, inside a zone or not, doesn't open the Mark bar for the zone
            editor.Document.Zones.Add(new MapZone() { Points = MapZone.Rectangle(90, -10, 110, 10) });
            editor.PointerDown(101, 1, MarkupButton.Left, false);
            editor.PointerUp(101, 1, false);
            Assert.Empty(asked);

            editor.PointerLeft();
            Assert.Null(editor.HoveredPet);
        }
    }
}
