namespace WorldEditor
{
    // A world can be named by its folder, its level.dat, or a .mcworld file: from the command line,
    // from a file manager, or from the list of recently opened worlds
    public static class WorldLocation
    {
        public static bool IsWorld(string path)
        {
            if (Directory.Exists(path)) return File.Exists(Path.Combine(path, "level.dat"));
            return File.Exists(path) && (McWorldArchive.IsArchive(path) || string.Equals(Path.GetFileName(path), "level.dat", StringComparison.OrdinalIgnoreCase));
        }

        // The folder holding the world's level.dat, extracting a .mcworld first; null if path isn't a world
        public static string? FindDirectory(string path)
        {
            if (!IsWorld(path)) return null;
            if (Directory.Exists(path)) return Path.GetFullPath(path);
            if (McWorldArchive.IsArchive(path)) return McWorldArchive.Extract(path);
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }

        public static Level? Read(string path, IObjectReader<string, Level?>? reader = null)
        {
            string? directory = FindDirectory(path);
            if (directory is null) return null;
            return (reader ?? new AnyEditionLevelReader()).Read(Path.Combine(directory, "level.dat"));
        }

        // The same world named two ways (a folder and its level.dat) compares equal; .mcworld files compare as files
        public static string Key(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(full), "level.dat", StringComparison.OrdinalIgnoreCase)) full = Path.GetDirectoryName(full) ?? full;
            return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        }
    }
}
