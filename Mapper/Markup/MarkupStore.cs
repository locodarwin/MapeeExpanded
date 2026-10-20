using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldEditor;

namespace Mapper
{
    // Keeps each world's markup in a file of its own in the settings folder ("Markup/<world>-<hash>.json"), found by
    // the path the world was opened from, the same way the recent worlds are. A world's own folder is never written
    // to: the game owns it, and a .mcworld is opened from a temporary copy
    public class MarkupStore
    {
        public string Folder { get; }

        public MarkupStore(string folder)
        {
            Folder = folder;
        }

        // Beside the settings file the GUIs use
        public static MarkupStore ForSettings(string settingsFile) => new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!, "Markup"));
        public static MarkupStore Default => ForSettings(AppSettings.DefaultFile);

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // The file's name starts with the world's name, for anyone looking in the folder, and ends with a hash of its
        // path, which is what finds it again: a world renamed in the game keeps its markup
        public string FileFor(string world, string? name = null)
        {
            string key = WorldLocation.Key(world);
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];

            if (Directory.Exists(Folder) && Directory.GetFiles(Folder, $"*-{hash}.json").FirstOrDefault() is string existing) return existing;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileName(key);
                if (string.Equals(Path.GetExtension(name), ".mcworld", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            }
            name = new string([.. name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ')]).Trim();
            if (name.Length > 40) name = name[..40].Trim();
            if (name.Length == 0) name = "world";

            return Path.Combine(Folder, $"{name}-{hash}.json");
        }

        // A world with no markup, or whose file can't be read, gets an empty document; the unreadable file is kept
        // aside rather than overwritten by the next save
        public MarkupDocument Load(string world, string? name = null)
        {
            string file = FileFor(world, name);
            MarkupDocument document = new() { World = Path.GetFullPath(world), Name = name };
            if (!File.Exists(file)) return document;

            try
            {
                document = Read(file);
                document.World = Path.GetFullPath(world);
                document.Name = name ?? document.Name;
                return document;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                Console.WriteLine($"Couldn't read markup from {file}: {e.Message}");
                try { File.Move(file, file + ".unreadable", true); } catch (IOException) { }
                return document;
            }
        }

        // Written beside the file and moved over it, so a crash mid-write keeps the markup saved before. A world
        // whose markup is all gone has its file removed
        public void Save(MarkupDocument document)
        {
            if (document.World is null) return;
            string file = FileFor(document.World, document.Name);

            try
            {
                if (document.IsEmpty)
                {
                    if (File.Exists(file)) File.Delete(file);
                    return;
                }
                Write(document, file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Couldn't save markup to {file}: {e.Message}");
            }
        }

        // Reads a markup file, as saved here or exported; throws if it isn't one
        public static MarkupDocument Read(string file)
        {
            MarkupDocument document = JsonSerializer.Deserialize<MarkupDocument>(File.ReadAllText(file), _options)
                ?? throw new InvalidDataException("The file is empty");
            if (document.Version > MarkupDocument.CurrentVersion) throw new InvalidDataException("The file was saved by a newer Mapee");

            document.Pins = [.. (document.Pins ?? []).Where(p => p is not null)];
            document.Zones = [.. (document.Zones ?? []).Where(z => z?.Points is { Count: >= 3 })];
            foreach (MarkupItem item in document.Pins.Cast<MarkupItem>().Concat(document.Zones))
            {
                if (string.IsNullOrEmpty(item.Id)) item.Id = MarkupItem.NewId();
                item.Label ??= string.Empty;
                item.Dimension ??= Dimension.Overworld.Namespace;
                item.Color = MarkupColors.All.Any(c => c.Hex == item.Color?.ToLowerInvariant()) ? item.Color!.ToLowerInvariant() : MarkupColors.Default;
            }
            foreach (MapPin pin in document.Pins)
            {
                if (PinIcons.PathOf(pin.Icon) is null) pin.Icon = null;
            }
            return document;
        }

        public static void Write(MarkupDocument document, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            string partial = file + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(document, _options));
            File.Move(partial, file, true);
        }
    }
}
