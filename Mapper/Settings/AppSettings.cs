using System.Text.Json;
using System.Text.Json.Serialization;
using WorldEditor;

namespace Mapper
{
    // What the GUIs remember between runs: the window, the style, which overlays are on, changed keys, and the worlds
    // opened recently with where the map was left in each. Kept in the user's settings folder, since the app's own
    // folder may be read-only (an AppImage is) or replaced by an update
    public class AppSettings
    {
        public const int MaxRecentWorlds = 12;

        public static string DefaultFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mapee", "Settings.json");

        // Where these settings were read from and are saved to
        [JsonIgnore]
        public string File { get; private set; } = DefaultFile;

        public WindowSettings Window { get; set; } = new();
        public string? Style { get; set; }
        public List<string> Overlays { get; set; } = [];
        // Markup is shown unless turned off, unlike the overlays
        public bool ShowMarkup { get; set; } = true;
        // Redraws what the game saves while a world is open, unless turned off
        public bool LiveReload { get; set; } = true;
        // Labels under the toolbar's icons where the window is wide enough, unless turned off (a right-click on the toolbar)
        public bool ToolbarLabels { get; set; } = true;
        public List<RecentWorld> RecentWorlds { get; set; } = [];
        // The shortcut keys changed from the built-in ones, by action name (ShortcutKeys)
        public Dictionary<string, string>? Keys { get; set; }

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // A missing or unreadable file gives the defaults; settings are never worth failing to start over
        public static AppSettings Load(string? file = null)
        {
            file ??= DefaultFile;
            try
            {
                if (!System.IO.File.Exists(file)) return new AppSettings() { File = file };
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(file), _options) ?? new AppSettings();
                settings.File = file;
                settings.Window ??= new();
                settings.Overlays ??= [];
                settings.RecentWorlds = [.. (settings.RecentWorlds ?? []).Where(w => !string.IsNullOrEmpty(w?.Path))];
                return settings;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.WriteLine($"Couldn't read settings from {file}: {e.Message}");
                return new AppSettings() { File = file };
            }
        }

        // Written beside the file and moved over it, so a crash mid-write leaves the old settings
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                string partial = File + ".partial";
                System.IO.File.WriteAllText(partial, JsonSerializer.Serialize(this, _options));
                System.IO.File.Move(partial, File, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Couldn't save settings to {File}: {e.Message}");
            }
        }

        public RecentWorld? FindRecent(string path)
        {
            string key = WorldLocation.Key(path);
            return RecentWorlds.FirstOrDefault(w => WorldLocation.Key(w.Path) == key);
        }

        // Moves the world to the front of the list, keeping where the map was left in it
        public RecentWorld Remember(string path)
        {
            RecentWorld world = FindRecent(path) ?? new RecentWorld();
            RecentWorlds.Remove(world);

            world.Path = Path.GetFullPath(path);
            world.Opened = DateTime.UtcNow;
            RecentWorlds.Insert(0, world);

            if (RecentWorlds.Count > MaxRecentWorlds) RecentWorlds.RemoveRange(MaxRecentWorlds, RecentWorlds.Count - MaxRecentWorlds);
            return world;
        }

        // Off the list only; the world itself is left alone
        public bool Forget(string path)
        {
            if (FindRecent(path) is not RecentWorld world) return false;
            RecentWorlds.Remove(world);
            return true;
        }

        public bool IsOverlayOn(string name) => Overlays.Contains(name);
        public void SetOverlay(string name, bool on)
        {
            Overlays.Remove(name);
            if (on) Overlays.Add(name);
        }
    }

    public class WindowSettings
    {
        // Device-independent units. Left and Top are only used where the window system lets apps place windows
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public bool Maximized { get; set; }
    }

    public class RecentWorld
    {
        // What was opened: the world folder, or the .mcworld file
        public string Path { get; set; } = string.Empty;
        public DateTime Opened { get; set; }
        // As the world called itself when last opened, for the Recent menu, which doesn't read the world
        public string? Name { get; set; }
        public string? Edition { get; set; }

        // Where the map was left: the dimension's namespace, and the block at the centre of the view
        public string? Dimension { get; set; }
        public double? X { get; set; }
        public double? Z { get; set; }
        public double? Zoom { get; set; }
    }
}
