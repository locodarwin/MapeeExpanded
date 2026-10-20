using System.Text.Json.Serialization;

namespace Mapper
{
    // What a user has marked on a world's map: labelled pins and labelled zones, each in one dimension. Kept apart
    // from the world itself (see MarkupStore), so the game's save folder is never written to
    public class MarkupDocument
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        // What the world was opened from when this was last saved; only informative, the file name is the key
        public string? World { get; set; }
        // The world's own name, which names the file when the world is first marked
        public string? Name { get; set; }

        public List<MapPin> Pins { get; set; } = [];
        public List<MapZone> Zones { get; set; } = [];

        [JsonIgnore]
        public bool IsEmpty => Pins.Count == 0 && Zones.Count == 0;

        public IEnumerable<MapPin> PinsIn(string dimension) => Pins.Where(p => p.Dimension == dimension);
        public IEnumerable<MapZone> ZonesIn(string dimension) => Zones.Where(z => z.Dimension == dimension);

        public MarkupDocument Clone() => new()
        {
            Version = Version,
            World = World,
            Name = Name,
            Pins = [.. Pins.Select(p => p.Clone())],
            Zones = [.. Zones.Select(z => z.Clone())]
        };

        // Brings in what another document has and this one doesn't, by id; returns how many pins and zones came in
        public (int Pins, int Zones) Merge(MarkupDocument other)
        {
            HashSet<string> ids = [.. Pins.Select(p => p.Id), .. Zones.Select(z => z.Id)];

            List<MapPin> pins = [.. other.Pins.Where(p => ids.Add(p.Id)).Select(p => p.Clone())];
            List<MapZone> zones = [.. other.Zones.Where(z => z.Points.Count >= 3 && ids.Add(z.Id)).Select(z => z.Clone())];

            Pins.AddRange(pins);
            Zones.AddRange(zones);
            return (pins.Count, zones.Count);
        }
    }

    // Shared by pins and zones, so the editor and the Places list can treat them alike
    public abstract class MarkupItem
    {
        public string Id { get; set; } = NewId();

        // The dimension's namespace, as the settings keep it ("minecraft:overworld")
        public string Dimension { get; set; } = "minecraft:overworld";

        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = MarkupColors.Default;
        public string? Note { get; set; }

        public static string NewId() => Guid.NewGuid().ToString("N")[..12];

        public abstract MarkupItem CopyItem();

        // Takes the user-editable fields from an edited copy
        public void TakeLook(MarkupItem from)
        {
            Label = from.Label;
            Color = from.Color;
            Note = string.IsNullOrWhiteSpace(from.Note) ? null : from.Note;
            if (this is MapPin pin && from is MapPin fromPin) pin.Icon = fromPin.Icon;
        }
    }

    // A point at one block
    public class MapPin : MarkupItem
    {
        public int X { get; set; }
        public int Z { get; set; }

        // One of PinIcons' ids, or null for a plain dot
        public string? Icon { get; set; }

        public MapPin Clone() => (MapPin)MemberwiseClone();
        public override MarkupItem CopyItem() => Clone();
    }

    // An area bounded by straight edges between block corners: a corner at (x, z) is the north-west corner of
    // block (x, z), so a zone holding blocks 0 to 15 east to west has corners at 0 and 16
    public class MapZone : MarkupItem
    {
        public List<MarkupPoint> Points { get; set; } = [];

        public MapZone Clone()
        {
            MapZone zone = (MapZone)MemberwiseClone();
            zone.Points = [.. Points];
            return zone;
        }
        public override MarkupItem CopyItem() => Clone();

        // A rectangle holding the blocks between two corners, given in either order, both included
        public static List<MarkupPoint> Rectangle(int x1, int z1, int x2, int z2)
        {
            int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2) + 1;
            int minZ = Math.Min(z1, z2), maxZ = Math.Max(z1, z2) + 1;
            return [new(minX, minZ), new(maxX, minZ), new(maxX, maxZ), new(minX, maxZ)];
        }
    }

    public record struct MarkupPoint(int X, int Z);

    // The colours markup can be drawn in, as "#rrggbb"; anything else read from a file falls back to the default
    public static class MarkupColors
    {
        public static readonly IReadOnlyList<(string Name, string Hex)> All =
        [
            ("Red", "#e53935"),
            ("Orange", "#fb8c00"),
            ("Yellow", "#fdd835"),
            ("Green", "#43a047"),
            ("Cyan", "#00acc1"),
            ("Blue", "#1e88e5"),
            ("Purple", "#8e24aa"),
            ("White", "#f5f5f5")
        ];

        public static string Default => All[0].Hex;

        public static (byte R, byte G, byte B) Parse(string? hex)
        {
            if (hex is { Length: 7 } && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint value))
            {
                return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }
            return Parse(Default);
        }

        // Light colours get dark glyphs and dark colours white ones
        public static bool IsLight(string? hex)
        {
            (byte r, byte g, byte b) = Parse(hex);
            return r * 0.299 + g * 0.587 + b * 0.114 > 170;
        }
    }

    // The symbols a pin can carry, as path data in a 16 by 16 box (SVG syntax, which WPF, Avalonia and browsers all
    // read), filled even-odd
    public static class PinIcons
    {
        public static readonly IReadOnlyList<(string Id, string Name, string Path)> All =
        [
            ("home", "Home", "M8 1.5 L15 8 H13 V14.5 H9.5 V10 H6.5 V14.5 H3 V8 H1 Z"),
            ("base", "Base", "M2.5 14.5 V3 H5 V5 H7 V3 H9 V5 H11 V3 H13.5 V14.5 H9.5 V11 A1.5 1.5 0 0 0 6.5 11 V14.5 Z"),
            ("portal", "Portal", "M3.5 1 H12.5 V15 H3.5 Z M5.5 3 V13 H10.5 V3 Z"),
            ("village", "Village", "M0.5 9 L4.5 5 L8.5 9 V14.5 H0.5 Z M7.5 5.5 L11.5 1.5 L15.5 5.5 V14.5 H9.5 V8.5 Z"),
            ("farm", "Farm", "M7.3 6 H8.7 V15 H7.3 Z M8 0.8 C10.2 2.8 10.2 4.8 8 6.6 C5.8 4.8 5.8 2.8 8 0.8 Z M3.5 5 C6.2 5.4 7.4 6.9 7.4 9.6 C4.8 9.2 3.6 7.7 3.5 5 Z M12.5 5 C12.4 7.7 11.2 9.2 8.6 9.6 C8.6 6.9 9.8 5.4 12.5 5 Z M3.5 9.6 C6.2 10 7.4 11.5 7.4 14.2 C4.8 13.8 3.6 12.3 3.5 9.6 Z M12.5 9.6 C12.4 12.3 11.2 13.8 8.6 14.2 C8.6 11.5 9.8 10 12.5 9.6 Z"),
            ("star", "Star", "M8 0.8 L10.1 5.6 L15.3 6.1 L11.4 9.6 L12.5 14.7 L8 12.1 L3.5 14.7 L4.6 9.6 L0.7 6.1 L5.9 5.6 Z"),
            ("flag", "Flag", "M2.5 1 H4.5 V15.5 H2.5 Z M4.5 1.5 H14 L11.5 5 L14 8.5 H4.5 Z"),
            ("danger", "Danger", "M8 1 L15.5 14.5 H0.5 Z M7 5.5 H9 L8.6 10.5 H7.4 Z M7 11.5 H9 V13.3 H7 Z")
        ];

        public static string? PathOf(string? id) => All.FirstOrDefault(i => i.Id == id).Path;
    }
}
