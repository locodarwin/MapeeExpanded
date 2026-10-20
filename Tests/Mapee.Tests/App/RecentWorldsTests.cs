using Mapper;

namespace Mapee.Tests.App
{
    // What the Recent menu shows for each world, taking worlds off the list, and what a file dropped on the window does
    public sealed class RecentWorldsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));
        private static readonly DateTime Now = new(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc);

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        // A folder that passes for a world: all that's looked at is a level.dat being there
        private string World(string folder, string? levelName = null)
        {
            string directory = Path.Combine(_folder, folder);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "level.dat"), [0]);
            if (levelName is not null) File.WriteAllText(Path.Combine(directory, "levelname.txt"), levelName + "\n");
            return directory;
        }

        private string FileIn(string name, string text = "{}")
        {
            Directory.CreateDirectory(_folder);
            string file = Path.Combine(_folder, name);
            File.WriteAllText(file, text);
            return file;
        }

        [Theory]
        [InlineData(0, "just now")]
        [InlineData(1, "1 minute ago")]
        [InlineData(45, "45 minutes ago")]
        [InlineData(60, "1 hour ago")]
        [InlineData(5 * 60, "5 hours ago")]
        public void SaysHowLongAgoWithinADay(int minutes, string expected)
        {
            Assert.Equal(expected, RecentWorlds.Ago(Now.AddMinutes(-minutes), Now));
        }

        [Fact]
        public void CountsDaysThenGivesTheDate()
        {
            Assert.Equal("3 days ago", RecentWorlds.Ago(Now.AddDays(-3), Now));
            Assert.Equal("13 days ago", RecentWorlds.Ago(Now.AddDays(-13), Now));
            Assert.Equal(Now.AddDays(-40).ToLocalTime().ToString("d MMM"), RecentWorlds.Ago(Now.AddDays(-40), Now));
            Assert.Equal(Now.AddYears(-1).ToLocalTime().ToString("d MMM yyyy"), RecentWorlds.Ago(Now.AddYears(-1), Now));
        }

        [Fact]
        public void NamesWorldsFromWhatWasKeptOrWhereTheyAre()
        {
            string bedrock = World("x+HPe5HdJCc=", "Myopic");
            string java = World("Survival");

            Assert.Equal("Kept name", RecentWorlds.Title(new RecentWorld() { Path = java, Name = "Kept name" }));
            Assert.Equal("Myopic", RecentWorlds.Title(new RecentWorld() { Path = bedrock }));
            Assert.Equal("Myopic", RecentWorlds.NameOf(Path.Combine(bedrock, "level.dat")));
            Assert.Equal("Survival", RecentWorlds.NameOf(java));
            Assert.Equal("Island", RecentWorlds.NameOf(FileIn("Island.mcworld")));
        }

        [Fact]
        public void RowsMarkTheOpenWorldAndTheMissingOnes()
        {
            List<RecentWorld> worlds =
            [
                new() { Path = "/worlds/a", Name = "Alpha", Edition = "Java", Opened = Now.AddHours(-2) },
                new() { Path = "/worlds/b", Name = "Beta", Edition = "Bedrock", Opened = Now.AddDays(-2) },
                new() { Path = "/worlds/gone", Name = "Gamma", Opened = Now.AddDays(-3) },
            ];

            List<RecentWorldRow> rows = RecentWorlds.Rows(worlds, "/worlds/b", Now, path => path != "/worlds/gone");

            Assert.Equal(["Alpha", "Beta", "Gamma"], rows.Select(r => r.Title));
            Assert.Equal("Java · 2 hours ago", rows[0].Detail);
            Assert.True(rows[1].Current);
            Assert.Equal("Open now · Bedrock · 2 days ago", rows[1].Detail);
            Assert.False(rows[2].Found);
            Assert.Equal("Not found where it was", rows[2].Detail);
        }

        [Fact]
        public void WorldsSharingANameAreToldApartByTheirFile()
        {
            List<RecentWorld> worlds =
            [
                new() { Path = "/worlds/Copy one.mcworld", Name = "Myopic", Edition = "Bedrock", Opened = Now },
                new() { Path = "/worlds/x+HPe5HdJCc=/", Name = "Myopic", Edition = "Bedrock", Opened = Now },
                new() { Path = "/worlds/Other", Name = "Other", Opened = Now },
            ];

            List<RecentWorldRow> rows = RecentWorlds.Rows(worlds, null, Now, path => true);

            Assert.Equal("Bedrock · just now · Copy one.mcworld", rows[0].Detail);
            Assert.Equal("Bedrock · just now · x+HPe5HdJCc=", rows[1].Detail);
            Assert.Equal("just now", rows[2].Detail);
        }

        [Fact]
        public void ForgettingAWorldOnlyTakesItOffTheList()
        {
            string world = World("Kept");
            AppSettings settings = AppSettings.Load(Path.Combine(_folder, "Settings.json"));
            settings.Remember(world);
            settings.Remember(World("Other"));

            Assert.True(settings.Forget(Path.Combine(world, "level.dat")));
            Assert.Equal(["Other"], settings.RecentWorlds.Select(w => Path.GetFileName(w.Path)));
            Assert.False(settings.Forget(world));
            Assert.True(File.Exists(Path.Combine(world, "level.dat")));
        }

        [Fact]
        public void TellsWhatADroppedFileWouldDo()
        {
            string world = World("Dropped", "Dropped world");

            DroppedItem folder = DroppedItem.Classify([world]);
            Assert.Equal(DropKind.World, folder.Kind);
            Assert.Equal("Drop to open Dropped world", folder.Why);
            Assert.Equal(DropKind.World, DroppedItem.Classify([Path.Combine(world, "level.dat")]).Kind);
            Assert.Equal(DropKind.World, DroppedItem.Classify([FileIn("Packed.mcworld")]).Kind);
            Assert.Equal(DropKind.Markup, DroppedItem.Classify([FileIn("Base markup.json")]).Kind);

            Assert.Equal(DropKind.None, DroppedItem.Classify([FileIn("notes.txt")]).Kind);
            Assert.Equal(DropKind.None, DroppedItem.Classify([Path.Combine(_folder, "Not a world")]).Kind);
            Assert.Equal("Drop one world at a time", DroppedItem.Classify([world, world]).Why);
            Assert.Equal(DropKind.None, DroppedItem.Classify([]).Kind);
        }
    }
}
