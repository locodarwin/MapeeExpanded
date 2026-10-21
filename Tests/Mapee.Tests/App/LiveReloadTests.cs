using Mapee.Tests.Fixtures;
using System.Buffers.Binary;
using System.Text;
using WorldEditor;

namespace Mapee.Tests.App
{
    // Live reload: finding what the game wrote since a world was opened, in a Bedrock database and in Java region files
    public sealed class LiveReloadTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

        private static List<string> ChangesBetween(LevelDbReader older, LevelDbReader newer)
        {
            List<string> keys = [];
            newer.ForEachChangeSince(older, key => true, key => keys.Add(Encoding.ASCII.GetString(key)));
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        // The second save rewrites the same table and log with the new writes numbered after the first save's
        [Theory]
        [InlineData(null)]
        [InlineData((byte)4)]
        public void FindsOnlyTheKeysWrittenSinceAnEarlierView(byte? tableCompression)
        {
            using BedrockTestWorld world = new();
            for (int i = 0; i < 10; i++) world.Put(Bytes($"key{i}"), Bytes("first"));
            world.Save(tableCompression: tableCompression);
            using LevelDbReader before = new(Path.Combine(world.Directory, "db"));

            world.Update(Bytes("key3"), Bytes("second")).Update(Bytes("new"), Bytes("x")).Delete(Bytes("key8")).Save(tableCompression: tableCompression);
            using LevelDbReader after = new(Path.Combine(world.Directory, "db"));

            Assert.Equal(["key3", "key8", "new"], ChangesBetween(before, after));
            Assert.Empty(ChangesBetween(after, after));
            Assert.True(after.LastSequence > before.LastSequence);
        }

        [Fact]
        public void BedrockWorldReopensWithTheNewChunksIndexed()
        {
            using BedrockTestWorld test = new BedrockTestWorld().Chunk(0, 0).Chunk(1, 0).Save();
            BedrockWorld first = BedrockWorld.Open(test.Directory);
            first.Indexed.Wait();
            Assert.Null(first.Reopen());

            // The game saves chunk 1,0 again and explores 40,0, in the next region over
            test.Update(BedrockTestWorld.Key(1, 0, 0, BedrockChunkTag.Data3D), [1])
                .Update(BedrockTestWorld.Key(40, 0, 0, BedrockChunkTag.Version), [40])
                .Save();

            var reopened = first.Reopen();
            Assert.NotNull(reopened);
            Assert.Equal([(0, 1, 0), (0, 40, 0)], reopened.Value.Changed.Order());
            Assert.True(reopened.Value.World.ChunkExists(0, 40, 0));
            Assert.True(reopened.Value.World.ChunkExists(0, 0, 0));
            Assert.Contains(new Coords(1, 0), reopened.Value.World.GetRegions(0).Keys);

            // A second store asking the first view later moves to the same newer one
            Assert.Same(reopened.Value.World, first.Reopen()!.Value.World);
            Assert.Null(reopened.Value.World.Reopen());
        }

        [Fact]
        public void BedrockStoreRedrawsRegionsAChangedChunksLightReaches()
        {
            using BedrockTestWorld test = new BedrockTestWorld().Chunk(31, 5).Chunk(10, 10).Save();
            BedrockRegionStore store = new(0);
            store.Itemize(test.Directory);
            store.Ready.Wait();
            string stamp = store.Stamp();
            Assert.Null(store.Refresh());

            test.Update(BedrockTestWorld.Key(31, 5, 0, BedrockChunkTag.Data3D), [1]).Save();
            Assert.NotEqual(stamp, store.Stamp());

            // Chunk 31 is on region 0's east edge, so region 1 is drawn again too
            RegionChanges? changes = store.Refresh();
            Assert.NotNull(changes);
            Assert.Equal(1, changes.Chunks);
            Assert.Equal([new Coords(0, 0), new Coords(1, 0)], changes.Regions.OrderBy(c => c.X).ThenBy(c => c.Z));
            Assert.Null(store.Refresh());
        }

        // An 8 KiB header: no chunk data, just a save time for each chunk given
        private string RegionFile(string name, int extraSectors, params (int Chunk, long Saved)[] chunks)
        {
            Directory.CreateDirectory(_folder);
            byte[] file = new byte[8192 + extraSectors * 4096];
            foreach (var (chunk, saved) in chunks) BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4096 + chunk * 4), (uint)saved);
            string path = Path.Combine(_folder, name);
            File.WriteAllBytes(path, file);
            return path;
        }

        [Fact]
        public void JavaStoreFindsChangedAndNewRegionFiles()
        {
            long longAgo = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds();
            RegionFile("r.0.0.mca", 0, (0, longAgo), (1, longAgo));
            RegionFile("r.5.5.mca", 0, (0, longAgo));

            AnvilRegionStore store = new();
            Assert.Equal(2, store.Itemize(_folder).Count());
            string stamp = store.Stamp();
            Assert.Null(store.Refresh());

            // The game saves two chunks of region 0,0 and starts region 1,0
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            RegionFile("r.0.0.mca", 2, (0, now), (1, longAgo), (2, now));
            RegionFile("r.1.0.mca", 1, (0, now));
            Assert.NotEqual(stamp, store.Stamp());
            Assert.False(store.Exists(new Coords(1, 0)));

            RegionChanges? changes = store.Refresh();
            Assert.NotNull(changes);
            Assert.Equal(3, changes.Chunks);
            Assert.Equal([new Coords(0, 0), new Coords(1, 0)], changes.Regions.OrderBy(c => c.X));
            Assert.True(store.Exists(new Coords(1, 0)));
            Assert.Null(store.Refresh());
        }

        [Fact]
        public void CountsChunksSavedSinceATime()
        {
            string file = RegionFile("r.0.0.mca", 0, (0, 1000), (5, 2000), (1023, 3000));
            Assert.Equal(2, AnvilRegionStore.ChunksSavedSince(file, 2000));
            Assert.Equal(0, AnvilRegionStore.ChunksSavedSince(file, 4000));
            Assert.Equal(0, AnvilRegionStore.ChunksSavedSince(RegionFile("r.1.0.mca", -1), 0));
        }
    }
}
