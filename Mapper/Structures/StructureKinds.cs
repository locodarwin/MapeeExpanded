namespace Mapper
{
    // A kind of structure: what it's called, its colour on the map, and how much it matters when labels crowd (lower
    // first)
    public record StructureKind(string Id, string Name, string Color, int Rank);

    // The game's structure names, Java's and Bedrock's, in the words the map uses
    public static class StructureKinds
    {
        public const string OtherColor = "#b0b0b0";

        private static readonly StructureKind[] Known =
        [
            new("stronghold", "Stronghold", "#9b7bff", 0),
            new("ancient_city", "Ancient city", "#2fb8a8", 1),
            new("mansion", "Woodland mansion", "#c47a3a", 2),
            new("monument", "Ocean monument", "#2ec4d6", 3),
            new("trial_chambers", "Trial chambers", "#e08a3c", 4),
            new("fortress", "Nether fortress", "#e0473c", 5),
            new("bastion_remnant", "Bastion", "#a08c78", 6),
            new("end_city", "End city", "#d6b3ff", 7),
            new("village", "Village", "#7bd35a", 8),
            new("pillager_outpost", "Pillager outpost", "#d9534f", 9),
            new("desert_pyramid", "Desert temple", "#e3c16f", 10),
            new("jungle_pyramid", "Jungle temple", "#8fbf4f", 11),
            new("swamp_hut", "Witch hut", "#a08fd0", 12),
            new("igloo", "Igloo", "#e8f4ff", 13),
            new("trail_ruins", "Trail ruins", "#c9a27e", 14),
            new("ruined_portal", "Ruined portal", "#c25bff", 15),
            new("shipwreck", "Shipwreck", "#5fa8d3", 16),
            new("ocean_ruin", "Ocean ruin", "#5fa8d3", 17),
            new("buried_treasure", "Buried treasure", "#f2c94c", 18),
        ];

        private static readonly Dictionary<string, StructureKind> ById = Known.ToDictionary(k => k.Id);

        // Everywhere, or nothing anyone goes looking for
        private static readonly HashSet<string> Skipped = ["mineshaft", "mineshaft_mesa", "nether_fossil", "invalid"];

        // Village kinds by their biome
        private static readonly Dictionary<string, string> Villages = new()
        {
            ["village_plains"] = "Plains village",
            ["village_desert"] = "Desert village",
            ["village_savanna"] = "Savanna village",
            ["village_snowy"] = "Snowy village",
            ["village_taiga"] = "Taiga village",
        };

        // Before 1.19 Java saved some under other names
        private static readonly Dictionary<string, string> Older = new()
        {
            ["endcity"] = "end_city",
            ["temple"] = "desert_pyramid",
        };

        // The kind and name for a saved structure id, such as minecraft:village_plains or Desert_Pyramid; null for
        // those left off the map
        public static (StructureKind Kind, string Name)? Of(string id)
        {
            string key = id.Trim().ToLowerInvariant();
            int colon = key.IndexOf(':');
            string space = colon < 0 ? "minecraft" : key[..colon];
            if (colon >= 0) key = key[(colon + 1)..];
            if (Skipped.Contains(key)) return null;
            if (Older.TryGetValue(key, out string? newer)) key = newer;

            if (space == "minecraft")
            {
                if (Villages.TryGetValue(key, out string? village)) return (ById["village"], village);
                if (ById.TryGetValue(key, out StructureKind? kind)) return (kind, kind.Name);
                foreach (string family in new[] { "village", "ruined_portal", "shipwreck", "ocean_ruin" })
                {
                    if (key.StartsWith(family + "_")) return (ById[family], ById[family].Name);
                }
            }

            // A data pack's or a newer game's: named from its id
            string name = key.Replace('_', ' ').Replace('/', ' ');
            name = name.Length == 0 ? id : char.ToUpperInvariant(name[0]) + name[1..];
            return (new StructureKind(colon < 0 ? key : $"{space}:{key}", name, OtherColor, 99), name);
        }

        public static StructureKind Get(string id) => ById[id];
    }
}
