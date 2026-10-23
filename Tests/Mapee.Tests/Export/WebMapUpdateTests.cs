using Mapee.Tests.Fixtures;
using Mapper;
using System.Buffers.Binary;
using WorldEditor;

namespace Mapee.Tests.Export
{
    // Updating an earlier web map: the fixed groups regions are drawn in, their fingerprints over the chunks' save
    // stamps, the stamps themselves for Java and Bedrock, and the writer keeping, forgetting and re-zooming tiles
    public sealed class WebMapUpdateTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        private static byte[] Solid(byte shade)
        {
            byte[] pixels = new byte[512 * 512 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = shade;
                pixels[i + 1] = (byte)(255 - shade);
                pixels[i + 2] = 40;
                pixels[i + 3] = 255;
            }
            return pixels;
        }

        private static void Add(WebMapWriter writer, Coords region, byte shade)
        {
            writer.AddRegion(region, region.X * 512, region.Z * 512, 512, 512, Solid(shade));
            short[] heights = new short[512 * 512];
            Array.Fill(heights, (short)shade);
            writer.AddHeights(region, heights);
        }

        // Every file under the dimension's folder but the manifest, by relative path
        private static Dictionary<string, byte[]> Files(string folder)
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != WebMapManifest.FileName)
                .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllBytes);
        }

        private static void AssertSameFiles(string expected, string actual)
        {
            Dictionary<string, byte[]> want = Files(expected), got = Files(actual);
            Assert.Equal(want.Keys.Order(StringComparer.Ordinal), got.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in want) Assert.True(bytes.AsSpan().SequenceEqual(got[path]), $"{path} differs");
        }

        private string Fresh(string name, Dictionary<Coords, byte> regions)
        {
            string folder = Path.Combine(_folder, name);
            WebMapWriter writer = new(folder, "overworld");
            foreach (var (region, shade) in regions) Add(writer, region, shade);
            writer.Finish("Overworld", "", []);
            return folder;
        }

        [Theory]
        // A region repainted, one gone, one new far out (a deeper zoom level)
        [InlineData(true)]
        // Most regions gone, so the map needs fewer levels than before
        [InlineData(false)]
        public void AnUpdateLeavesTheSameFilesAsAFreshExport(bool grow)
        {
            Dictionary<Coords, byte> before = [];
            for (int x = -3; x <= 4; x++)
            {
                for (int z = -2; z <= 2; z++) before[new Coords(x, z)] = (byte)(x * 20 + z * 7 + 100);
            }

            Dictionary<Coords, byte> after = new(before);
            if (grow)
            {
                after[new Coords(1, 1)] = 3;
                after.Remove(new Coords(-3, -2));
                after[new Coords(40, 9)] = 200;
            }
            else
            {
                foreach (Coords region in before.Keys.Where(c => c.X > -2 || c.Z != 0)) after.Remove(region);
            }

            string updated = Fresh("updated", before);
            string expected = Fresh("expected", after);

            // As the exporter does it: keep what's unchanged, forget the rest, draw the changed ones again
            HashSet<Coords> changed = [.. before.Keys.Concat(after.Keys).Where(c => !before.TryGetValue(c, out byte a) || !after.TryGetValue(c, out byte b) || a != b)];
            WebMapWriter writer = new(updated, "overworld");
            writer.Keep(before.Keys.Where(c => !changed.Contains(c)));
            foreach (Coords region in changed) writer.Forget(region);
            foreach (Coords region in changed.Where(after.ContainsKey)) Add(writer, region, after[region]);
            WebMapDimension dimension = writer.Finish("Overworld", "", [], changed: changed);

            AssertSameFiles(expected, updated);
            Assert.Equal(after.Count, dimension.Regions.Count);
        }

        [Fact]
        public void AnUpdateRemakesOnlyTheZoomTilesOverWhatChanged()
        {
            Dictionary<Coords, byte> regions = [];
            for (int x = 0; x < 8; x++) regions[new Coords(x, 0)] = (byte)(x * 30);
            string folder = Fresh("map", regions);

            string far = Path.Combine(folder, "overworld", "tiles", "-1", "3_0.png");
            string near = Path.Combine(folder, "overworld", "tiles", "-1", "0_0.png");
            DateTime old = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(far, old);
            File.SetLastWriteTimeUtc(near, old);

            WebMapWriter writer = new(folder, "overworld");
            writer.Keep(regions.Keys.Where(c => c.X != 0));
            writer.Forget(new Coords(0, 0));
            Add(writer, new Coords(0, 0), 99);
            writer.Finish("Overworld", "", [], changed: new HashSet<Coords> { new(0, 0) });

            Assert.Equal(old, File.GetLastWriteTimeUtc(far));
            Assert.NotEqual(old, File.GetLastWriteTimeUtc(near));
        }

        [Fact]
        public void GroupsAreUpToFourRegionsInARowFromAMultipleOfFour()
        {
            Assert.Equal(new Coords(-1, 0), WebMapGroups.Of(new Coords(-1, 0)));
            Assert.Equal(new Coords(-1, 0), WebMapGroups.Of(new Coords(-4, 0)));
            Assert.Equal(new Coords(0, 3), WebMapGroups.Of(new Coords(3, 3)));
            Assert.Equal(new Coords(1, -2), WebMapGroups.Of(new Coords(4, -2)));

            List<Coords[]> groups = WebMapGroups.Group([new(5, 0), new(-1, 0), new(0, 0), new(3, 0), new(2, -1), new(4, 0)]);
            Assert.Equal([[new(2, -1)], [new(-1, 0)], [new(0, 0), new(3, 0)], [new(4, 0), new(5, 0)]], groups);
        }

        [Fact]
        public void AFingerprintSeesSavesInTheGroupAndTheRingAroundItOnly()
        {
            Coords[] group = [new(0, 0), new(1, 0)];
            Dictionary<Coords, ulong> stamps = new() { [new(5, 5)] = 1, [new(40, 3)] = 2, [new(-2, 0)] = 3, [new(200, 0)] = 4 };
            string before = WebMapGroups.Fingerprint(group, stamps);

            string Changed(Coords chunk)
            {
                Dictionary<Coords, ulong> copy = new(stamps);
                copy[chunk] = copy.GetValueOrDefault(chunk) + 10;
                return WebMapGroups.Fingerprint(group, copy);
            }

            Assert.NotEqual(before, Changed(new(40, 3)));
            // The ring: one chunk past each edge of the four region slots
            Assert.NotEqual(before, Changed(new(-1, 31)));
            Assert.NotEqual(before, Changed(new(128, -1)));
            Assert.NotEqual(before, Changed(new(64, 32)));
            Assert.Equal(before, Changed(new(-2, 0)));
            Assert.Equal(before, Changed(new(129, 0)));
            Assert.Equal(before, Changed(new(0, 33)));

            // A region joining the group, even an empty one
            Assert.NotEqual(before, WebMapGroups.Fingerprint([.. group, new(3, 0)], stamps));
        }

        [Fact]
        public void BedrockStampsMoveWhenAChunkIsDrawnDifferentlyOnly()
        {
            using BedrockTestWorld test = new BedrockTestWorld().Chunk(0, 0).Chunk(1, 0).Chunk(0, 0, dimension: 1)
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 2), [9]).Save(tableCompression: 4);
            IReadOnlyDictionary<Coords, ulong> before = BedrockWorld.Open(test.Directory).ChunkStamps(0);
            Assert.Equal([new(0, 0), new(1, 0)], before.Keys.OrderBy(c => c.X));
            Assert.Single(BedrockWorld.Open(test.Directory).ChunkStamps(1));

            // Ticks and block entities aren't drawn; a sub-chunk is
            test.Update(BedrockTestWorld.Key(1, 0, 0, BedrockChunkTag.PendingTicks), [1])
                .Update(BedrockTestWorld.Key(1, 0, 0, BedrockChunkTag.BlockEntity), [1])
                .Update(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.SubChunkPrefix, 2), [8])
                .Save(tableCompression: 4);
            IReadOnlyDictionary<Coords, ulong> after = BedrockWorld.Open(test.Directory).ChunkStamps(0);

            Assert.True(after[new(0, 0)] > before[new(0, 0)]);
            Assert.Equal(before[new(1, 0)], after[new(1, 0)]);
        }

        [Fact]
        public void BedrockStampsCountADeletionAsAChange()
        {
            using BedrockTestWorld test = new BedrockTestWorld().Chunk(0, 0)
                .Put(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.Data3D), [1]).Save(tableCompression: 4);
            ulong before = BedrockWorld.Open(test.Directory).ChunkStamps(0)[new(0, 0)];

            test.Delete(BedrockTestWorld.Key(0, 0, 0, BedrockChunkTag.Data3D)).Save(tableCompression: 4);
            Assert.True(BedrockWorld.Open(test.Directory).ChunkStamps(0)[new(0, 0)] > before);
        }

        [Fact]
        public void JavaStampsComeFromTheRegionHeader()
        {
            Dictionary<string, object> chunk = new() { ["DataVersion"] = JavaTestWorld.DataVersion };
            using JavaTestWorld test = new JavaTestWorld().Chunk(0, 0, chunk).Chunk(1, 0, chunk).Chunk(-1, -33, chunk).Save();
            string folder = Path.Combine(test.Directory, "dimensions", "minecraft", "overworld", "region");

            AnvilRegionStore store = new();
            _ = store.Itemize(folder).ToList();
            IReadOnlyDictionary<Coords, ulong> before = store.ChunkStamps();
            Assert.Equal([new(-1, -33), new(0, 0), new(1, 0)], before.Keys.OrderBy(c => c.X));

            // The game saves chunk 1,0: a new time in the second 4 KiB of the header
            string file = Path.Combine(folder, "r.0.0.mca");
            byte[] bytes = File.ReadAllBytes(file);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4096 + 4), 1_800_000_000);
            File.Delete(file);
            File.WriteAllBytes(file, bytes);

            IReadOnlyDictionary<Coords, ulong> after = store.ChunkStamps();
            Assert.NotEqual(before[new(1, 0)], after[new(1, 0)]);
            Assert.Equal(before[new(0, 0)], after[new(0, 0)]);
            Assert.Equal(before[new(-1, -33)], after[new(-1, -33)]);
        }

        [Fact]
        public void TheManifestIsKeptOnlyForTheSameWorldDrawnTheSameWay()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "nether"));
            WebMapIdentity identity = new("Bedrock|World|42", "ABC");
            new WebMapManifest
            {
                Format = WebMapManifest.CurrentFormat,
                World = identity.World,
                Drawing = identity.Drawing,
                Groups = new() { ["0_0"] = "F00D" },
                Regions = [0, 0, 3, -1]
            }.Write(_folder, "nether");

            WebMapManifest? read = WebMapManifest.Read(_folder, "nether");
            Assert.NotNull(read);
            Assert.True(read.Matches(identity));
            Assert.False(read.Matches(identity with { Drawing = "ABD" }));
            Assert.False(read.Matches(identity with { World = "Bedrock|World|43" }));
            Assert.Equal([new(0, 0), new(3, -1)], read.DrawnRegions());
            Assert.Equal("F00D", read.Groups["0_0"]);

            // Clearing the dimension takes the manifest with it, so the next export draws everything
            WebMapFolder.ClearDimension(_folder, "nether");
            Assert.Null(WebMapManifest.Read(_folder, "nether"));

            File.WriteAllText(Path.Combine(_folder, "nether", WebMapManifest.FileName), "{ not json");
            Assert.Null(WebMapManifest.Read(_folder, "nether"));
        }
    }
}
