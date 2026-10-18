using Mapee.Tests.Fixtures;
using System.IO.Compression;
using WorldEditor;

namespace Mapee.Tests.App
{
    // How a world named on the command line or in the recent list is found
    public sealed class WorldLocationTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void FindsTheWorldFromItsFolderOrLevelDat()
        {
            using BedrockTestWorld world = new BedrockTestWorld().Chunk(0, 0).Save("Named");

            Assert.Equal(Path.GetFullPath(world.Directory), WorldLocation.FindDirectory(world.Directory));
            Assert.Equal(Path.GetFullPath(world.Directory), WorldLocation.FindDirectory(world.LevelDat));
            Assert.Equal("Named", WorldLocation.Read(world.LevelDat)?.WorldName);
            Assert.Equal(WorldLocation.Key(world.Directory), WorldLocation.Key(world.LevelDat));
        }

        [Fact]
        public void ExtractsAnMcworld()
        {
            using BedrockTestWorld world = new BedrockTestWorld().Chunk(0, 0).Save("Packed");
            Directory.CreateDirectory(_folder);
            string archive = Path.Combine(_folder, "Packed.mcworld");
            ZipFile.CreateFromDirectory(world.Directory, archive);
            string cache = Path.Combine(_folder, "cache");

            string? extracted = WorldLocation.FindDirectory(archive, cache);
            Assert.NotNull(extracted);
            Assert.StartsWith(cache, extracted);
            Assert.True(File.Exists(Path.Combine(extracted, "level.dat")));

            // Unchanged archives are reused rather than extracted again
            Assert.Equal(extracted, WorldLocation.FindDirectory(archive, cache));
        }

        [Fact]
        public void RejectsWhatIsNotAWorld()
        {
            Directory.CreateDirectory(_folder);
            string text = Path.Combine(_folder, "notes.txt");
            File.WriteAllText(text, "hello");

            Assert.Null(WorldLocation.FindDirectory(_folder));
            Assert.Null(WorldLocation.FindDirectory(text));
            Assert.Null(WorldLocation.FindDirectory(Path.Combine(_folder, "missing")));
            Assert.Null(WorldLocation.Read(text));
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
    }
}
