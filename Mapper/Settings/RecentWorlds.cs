using WorldEditor;

namespace Mapper
{
    // What the Recent menu says about a world it remembers, without reading the world: the name and edition are kept
    // when it's opened, and older entries fall back to the name of what was opened
    public static class RecentWorlds
    {
        public static string Title(RecentWorld world) => world.Name is { Length: > 0 } name ? name : NameOf(world.Path);

        // "Bedrock · 2 days ago"
        public static string Detail(RecentWorld world, DateTime nowUtc)
        {
            string ago = Ago(world.Opened, nowUtc);
            return world.Edition is { Length: > 0 } edition ? $"{edition} · {ago}" : ago;
        }

        // The Recent menu's rows, in the list's order. found says whether a path still holds a world (passed in so tests
        // needn't make worlds); worlds sharing a name get the file or folder they're in after their detail
        public static List<RecentWorldRow> Rows(IReadOnlyList<RecentWorld> worlds, string? current, DateTime nowUtc, Func<string, bool> found)
        {
            Dictionary<string, int> titles = worlds.GroupBy(Title).ToDictionary(g => g.Key, g => g.Count());
            List<RecentWorldRow> rows = [];
            foreach (RecentWorld world in worlds)
            {
                string title = Title(world);
                bool here = IsSame(world.Path, current);
                bool exists = here || found(world.Path);

                string detail = exists ? Detail(world, nowUtc) : "Not found where it was";
                if (titles[title] > 1) detail += " · " + Place(world.Path);
                if (here) detail = "Open now · " + detail;
                rows.Add(new(world, title, detail, exists, here));
            }
            return rows;
        }

        public static bool IsSame(string path, string? other)
        {
            try
            {
                return other is not null && WorldLocation.Key(path) == WorldLocation.Key(other);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        // The file or folder a world was opened from, by name
        private static string Place(string path) => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path;

        public static string Ago(DateTime openedUtc, DateTime nowUtc)
        {
            TimeSpan since = nowUtc - openedUtc;
            if (since < TimeSpan.FromMinutes(1)) return "just now";
            if (since < TimeSpan.FromHours(1)) return Count((int)since.TotalMinutes, "minute") + " ago";
            if (since < TimeSpan.FromDays(1)) return Count((int)since.TotalHours, "hour") + " ago";

            int days = (nowUtc.ToLocalTime().Date - openedUtc.ToLocalTime().Date).Days;
            if (days <= 1) return "yesterday";
            if (days < 14) return $"{days} days ago";
            return openedUtc.ToLocalTime().ToString(openedUtc.Year == nowUtc.Year ? "d MMM" : "d MMM yyyy");
        }

        private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

        // A world's name from where it is, for worlds opened before names were kept and for files being dragged in:
        // Bedrock folders are random ids with the name in levelname.txt, Java folders are named after the world
        public static string NameOf(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (File.Exists(full) && McWorldArchive.IsArchive(full)) return Path.GetFileNameWithoutExtension(full);
                if (string.Equals(Path.GetFileName(full), "level.dat", StringComparison.OrdinalIgnoreCase)) full = Path.GetDirectoryName(full) ?? full;

                string levelName = Path.Combine(full, "levelname.txt");
                if (File.Exists(levelName) && File.ReadLines(levelName).FirstOrDefault()?.Trim() is { Length: > 0 } name) return name;
                return Path.GetFileName(full) is { Length: > 0 } folder ? folder : full;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return Path.GetFileName(path);
            }
        }
    }

    public record RecentWorldRow(RecentWorld World, string Title, string Detail, bool Found, bool Current);

    public enum DropKind { None, World, Markup }

    // What a file dragged onto the window would do: open a world, add a markup file's pins and zones, or nothing
    public record DroppedItem(DropKind Kind, string? Path, string Why)
    {
        public static DroppedItem Classify(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0) return new(DropKind.None, null, "Nothing to open in that");
            if (paths.Count > 1) return new(DropKind.None, null, "Drop one world at a time");

            string path = paths[0];
            if (WorldLocation.IsWorld(path)) return new(DropKind.World, path, $"Drop to open {RecentWorlds.NameOf(path)}");
            if (File.Exists(path) && string.Equals(System.IO.Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return new(DropKind.Markup, path, "Drop to add its pins and zones to this world");
            }
            return new(DropKind.None, null, "Mapee opens world folders, level.dat and .mcworld files, and markup .json files");
        }
    }
}
