using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WorldEditor
{
    // A .mcworld file is a zip of a Bedrock world folder. Mapee reads worlds from folders, so the archive
    // is extracted into a cache, reused while the file is unchanged, and old extractions are pruned
    public static class McWorldArchive
    {
        public const string Extension = ".mcworld";

        public static string DefaultCacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mapee", "mcworld");

        public static bool IsArchive(string file)
        {
            return string.Equals(Path.GetExtension(file), Extension, StringComparison.OrdinalIgnoreCase);
        }

        // Returns the folder holding the world's level.dat
        public static string Extract(string file, string? cacheDirectory = null, int keep = 5)
        {
            cacheDirectory ??= DefaultCacheDirectory;
            Directory.CreateDirectory(cacheDirectory);

            FileInfo info = new(file);
            string stamp = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..12].ToLowerInvariant();
            string target = Path.Combine(cacheDirectory, $"{SafeName(Path.GetFileNameWithoutExtension(file))}-{hash}");

            string? world = FindWorld(target);
            if (world is null)
            {
                // Extract beside the target and move it into place, so an interrupted extraction is never reused
                string partial = target + ".partial";
                if (Directory.Exists(partial)) Directory.Delete(partial, true);
                if (Directory.Exists(target)) Directory.Delete(target, true);

                ZipFile.ExtractToDirectory(file, partial);
                Directory.Move(partial, target);

                world = FindWorld(target) ?? throw new InvalidDataException($"{Path.GetFileName(file)} doesn't contain a level.dat");
            }

            Directory.SetLastWriteTimeUtc(target, DateTime.UtcNow);
            Prune(cacheDirectory, target, keep);
            return world;
        }

        // Exports normally have level.dat at the root, but some tools zip the folder itself
        private static string? FindWorld(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            if (File.Exists(Path.Combine(directory, "level.dat"))) return directory;

            return Directory.GetDirectories(directory).FirstOrDefault(sub => File.Exists(Path.Combine(sub, "level.dat")));
        }

        private static void Prune(string cacheDirectory, string current, int keep)
        {
            IEnumerable<DirectoryInfo> old = new DirectoryInfo(cacheDirectory).GetDirectories()
                .Where(x => x.FullName != Path.GetFullPath(current))
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep - 1));

            foreach (DirectoryInfo directory in old)
            {
                try
                {
                    directory.Delete(true);
                }
                catch (IOException)
                {
                    // Still open in this or another Mapee; try again next time
                }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string SafeName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string output = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return output.Length > 40 ? output[..40] : output;
        }
    }
}
