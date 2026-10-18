using System.Text.Json;

namespace WorldEditor
{
    // How much light each (Java-named) block gives off and how much it filters, from Translators/Bedrock/Light.json.
    // The table holds each block's default; a few blocks only glow in some states, which is worked out here
    public class BedrockLightTable
    {
        public static string DefaultFile => Path.Combine("Resources", "WorldEditor", "Translators", "Bedrock", "Light.json");

        private static readonly Lazy<BedrockLightTable> _default = new(() => FromFile(DefaultFile));
        public static BedrockLightTable Default => _default.Value;

        private readonly Dictionary<string, (byte Emit, byte Filter)> _blocks;

        // Blocks the table lists as dark because they only glow while lit
        private static readonly Dictionary<string, byte> _litEmission = new()
        {
            ["minecraft:furnace"] = 13,
            ["minecraft:blast_furnace"] = 13,
            ["minecraft:smoker"] = 13,
            ["minecraft:redstone_lamp"] = 15,
            ["minecraft:redstone_ore"] = 9,
            ["minecraft:deepslate_redstone_ore"] = 9,
            ["minecraft:candle_cake"] = 3
        };

        public BedrockLightTable(Dictionary<string, (byte Emit, byte Filter)> blocks)
        {
            _blocks = blocks;
        }

        public static BedrockLightTable FromFile(string file)
        {
            Dictionary<string, int[]>? blocks = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(file));
            return new BedrockLightTable((blocks ?? []).ToDictionary(x => x.Key, x => ((byte)x.Value[0], (byte)x.Value[1])));
        }

        // Unknown blocks are treated as solid and dark
        public byte Filter(Block block)
        {
            return _blocks.TryGetValue(block.Name, out var entry) ? entry.Filter : (byte)15;
        }

        public byte Emission(Block block)
        {
            byte emit = _blocks.TryGetValue(block.Name, out var entry) ? entry.Emit : (byte)0;

            string? lit = Property(block, "lit");
            if (lit == "false") return 0;
            if (lit == "true")
            {
                if (block.Name.EndsWith("candle")) return (byte)(3 * ParseOr(Property(block, "candles"), 1));
                if (emit == 0 && _litEmission.TryGetValue(block.Name, out byte litEmit)) return litEmit;
            }

            if (block.Name == "minecraft:light") return (byte)ParseOr(Property(block, "level"), emit);
            return emit;
        }

        private static string? Property(Block block, string name)
        {
            foreach (Property property in block.Properties)
            {
                if (property.Name == name) return property.Value;
            }
            return null;
        }
        private static int ParseOr(string? value, int fallback)
        {
            return int.TryParse(value, out int output) ? output : fallback;
        }
    }
}
