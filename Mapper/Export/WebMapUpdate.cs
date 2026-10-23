using System.Security.Cryptography;
using System.Text.Json;
using WorldEditor;

namespace Mapper
{
    // What a dimension of a web map is drawn from and how: which world (the same one even when copied or opened from
    // a fresh .mcworld) and every setting that changes the picture. An earlier export's regions are kept only when
    // both match
    public record WebMapIdentity(string World, string Drawing);

    // Regions are drawn in fixed groups: up to four side by side in a row, starting at an X that's a multiple of
    // four. Shading looks only at the regions drawn alongside, so this way a region always has the same neighbours,
    // whatever else the world holds, and drawing one group again gives the pixels a whole export would
    public static class WebMapGroups
    {
        public const int Width = 4;

        public static Coords Of(Coords region) => new(MathUtilities.FindSectionY(region.X, Width), region.Z);

        // The groups the regions fall in, rows from the north, each west to east
        public static List<Coords[]> Group(IEnumerable<Coords> regions)
        {
            return [.. regions.GroupBy(Of).OrderBy(g => g.Key.Z).ThenBy(g => g.Key.X).Select(g => g.OrderBy(c => c.X).ToArray())];
        }

        // Changes when the group gains or loses a region, or when a chunk in it, or in the ring of chunks just
        // outside whose light reaches in, is saved, made or removed
        public static string Fingerprint(Coords[] group, IReadOnlyDictionary<Coords, ulong> stamps)
        {
            Coords key = Of(group[0]);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[16];

            foreach (Coords region in group)
            {
                BitConverter.TryWriteBytes(buffer, region.X);
                BitConverter.TryWriteBytes(buffer[4..], region.Z);
                hash.AppendData(buffer[..8]);
            }

            int left = key.X * Width * 32 - 1, top = key.Z * 32 - 1;
            for (int z = top; z <= top + 33; z++)
            {
                for (int x = left; x <= left + Width * 32 + 1; x++)
                {
                    if (!stamps.TryGetValue(new Coords(x, z), out ulong stamp)) continue;

                    BitConverter.TryWriteBytes(buffer, x);
                    BitConverter.TryWriteBytes(buffer[4..], z);
                    BitConverter.TryWriteBytes(buffer[8..], stamp);
                    hash.AppendData(buffer);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset(), 0, 16);
        }

        public static string Name(Coords group) => $"{group.X}_{group.Z}";
    }

    // Left in each dimension's folder by an export: what it drew and from what, so the next export into the folder
    // draws only the groups whose fingerprints differ. Removed when an update starts and written again when it ends,
    // so an update that stops halfway leaves none, and the next export draws everything
    public class WebMapManifest
    {
        public const int CurrentFormat = 1;
        public const string FileName = "mapee-export.json";

        public int Format { get; set; }
        public string World { get; set; } = string.Empty;
        public string Drawing { get; set; } = string.Empty;

        // Group name (WebMapGroups.Name) to fingerprint
        public Dictionary<string, string> Groups { get; set; } = [];

        // The regions that have a tile, as x, z pairs
        public int[] Regions { get; set; } = [];

        private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public bool Matches(WebMapIdentity identity) => Format == CurrentFormat && World == identity.World && Drawing == identity.Drawing;

        public IEnumerable<Coords> DrawnRegions()
        {
            for (int i = 0; i + 1 < Regions.Length; i += 2) yield return new Coords(Regions[i], Regions[i + 1]);
        }

        // Null when there's none or it can't be read
        public static WebMapManifest? Read(string directory, string id)
        {
            try
            {
                string path = PathOf(directory, id);
                return File.Exists(path) ? JsonSerializer.Deserialize<WebMapManifest>(File.ReadAllText(path), Options) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        public void Write(string directory, string id)
        {
            string path = PathOf(directory, id);
            File.Delete(path);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }

        public static void Delete(string directory, string id)
        {
            string path = PathOf(directory, id);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string PathOf(string directory, string id) => Path.Combine(directory, id, FileName);
    }
}
