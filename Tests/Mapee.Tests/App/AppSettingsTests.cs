using Mapper;

namespace Mapee.Tests.App
{
    public sealed class AppSettingsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));
        private string File => Path.Combine(_folder, "Settings.json");

        [Fact]
        public void MissingOrBrokenFileGivesDefaults()
        {
            AppSettings missing = AppSettings.Load(File);
            Assert.Empty(missing.RecentWorlds);
            Assert.Null(missing.Style);
            Assert.Equal(File, missing.File);

            Directory.CreateDirectory(_folder);
            System.IO.File.WriteAllText(File, "{ not json");
            AppSettings broken = AppSettings.Load(File);
            Assert.Empty(broken.Overlays);
            Assert.Equal(File, broken.File);
        }

        [Fact]
        public void SurvivesSavingAndLoading()
        {
            AppSettings settings = AppSettings.Load(File);
            settings.Style = "caves";
            settings.SetOverlay("grid", true);
            settings.SetOverlay("axes", true);
            settings.SetOverlay("grid", false);
            settings.Window.Width = 1100;
            settings.Window.Maximized = true;
            RecentWorld recent = settings.Remember(Path.Combine(_folder, "World"));
            recent.Dimension = "minecraft:nether";
            recent.X = 12.5;
            recent.Z = -40;
            recent.Zoom = 3;
            settings.Save();

            AppSettings loaded = AppSettings.Load(File);
            Assert.Equal("caves", loaded.Style);
            Assert.Equal(["axes"], loaded.Overlays);
            Assert.Equal(1100, loaded.Window.Width);
            Assert.True(loaded.Window.Maximized);
            RecentWorld world = Assert.Single(loaded.RecentWorlds);
            Assert.Equal(("minecraft:nether", 12.5, -40.0, 3.0), (world.Dimension, world.X!.Value, world.Z!.Value, world.Zoom!.Value));
            Assert.False(System.IO.File.Exists(File + ".partial"));
        }

        [Fact]
        public void RecentWorldsAreNewestFirstWithoutDuplicates()
        {
            AppSettings settings = AppSettings.Load(File);
            string a = Path.Combine(_folder, "A"), b = Path.Combine(_folder, "B");

            settings.Remember(a).X = 5;
            settings.Remember(b);
            settings.Remember(Path.Combine(a, "level.dat"));

            Assert.Equal(2, settings.RecentWorlds.Count);
            Assert.Equal(5, settings.RecentWorlds[0].X);
            Assert.Same(settings.RecentWorlds[0], settings.FindRecent(a + Path.DirectorySeparatorChar));

            for (int i = 0; i < 20; i++) settings.Remember(Path.Combine(_folder, $"World {i}"));
            Assert.Equal(AppSettings.MaxRecentWorlds, settings.RecentWorlds.Count);
            Assert.EndsWith("World 19", settings.RecentWorlds[0].Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
    }
}
