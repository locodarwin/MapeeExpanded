using System.Collections.Concurrent;
using System.Text.Json;

namespace WorldEditor
{
    // Translates Bedrock block states to the Java states Mapee's assets are written for.
    // Exact states come from Translators/Bedrock/Blocks.json, then states older versions saved from
    // LegacyBlocks.json; a state missing from both falls back to the Java name its block maps to,
    // without properties; an unknown block keeps its Bedrock name.
    // Worlds before 1.13 store a data value instead of states, written here as a single "val" property
    // (see Legacy), and before 1.2.13 a numeric ID, written as the ID in place of the name
    public class BedrockBlockTranslator
    {
        public static string DefaultFile => Path.Combine("Resources", "WorldEditor", "Translators", "Bedrock", "Blocks.json");
        public static string DefaultLegacyFile => Path.Combine("Resources", "WorldEditor", "Translators", "Bedrock", "LegacyBlocks.json");
        public const string ValueProperty = "val";

        private static readonly Lazy<BedrockBlockTranslator> _default = new(() => FromFile(DefaultFile, DefaultLegacyFile));
        public static BedrockBlockTranslator Default => _default.Value;

        private readonly Dictionary<string, string> _states;
        private readonly Dictionary<string, string> _names = [];
        private readonly ConcurrentDictionary<string, Block> _cache = new();

        // Only worlds with old chunks need the legacy table, so it's read on the first state Blocks.json lacks
        private readonly Lazy<LegacyTable> _legacy;

        public BedrockBlockTranslator(Dictionary<string, string> states, Func<LegacyTable>? legacy = null)
        {
            _states = states;
            foreach (var state in states)
            {
                _names.TryAdd(NameOf(state.Key), NameOf(state.Value));
            }
            _legacy = new(legacy ?? (() => new LegacyTable()));
        }

        public static BedrockBlockTranslator FromFile(string file, string? legacyFile = null)
        {
            Dictionary<string, string>? states = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            return new BedrockBlockTranslator(states ?? [], legacyFile is null || !File.Exists(legacyFile) ? null : () => LegacyTable.FromFile(legacyFile));
        }

        // A block from a palette that stores a data value, or with a numeric name, from a chunk that stores IDs
        public static Block Legacy(string name, int value)
        {
            return new Block(name, [new Property(ValueProperty, value.ToString())]);
        }

        public Block Translate(Block bedrock)
        {
            string key = StateKey(bedrock);
            if (_cache.TryGetValue(key, out Block cached)) return cached;

            Block output;
            if (_states.TryGetValue(key, out string? java)) output = ParseState(java);
            else if (TranslateLegacy(bedrock, key) is string legacy) output = ParseState(legacy);
            else if (_names.TryGetValue(bedrock.Name, out string? javaName)) output = new Block(javaName);
            else if (_legacy.Value.Names.TryGetValue(bedrock.Name, out string? legacyName)) output = new Block(legacyName);
            else output = bedrock;

            _cache[key] = output;
            return output;
        }

        private string? TranslateLegacy(Block bedrock, string key)
        {
            LegacyTable legacy = _legacy.Value;
            if (bedrock.Properties.Length == 1 && bedrock.Properties[0].Name == ValueProperty)
            {
                string name = bedrock.Name;
                if (name.Length > 0 && char.IsAsciiDigit(name[0]) && !legacy.Ids.TryGetValue(name, out name!)) return null;

                // Bits a block doesn't use are sometimes set; its plain form is closer than nothing
                return legacy.Values.GetValueOrDefault($"{name}@{bedrock.Properties[0].Value}") ?? legacy.Values.GetValueOrDefault($"{name}@0");
            }
            return legacy.States.GetValueOrDefault(key);
        }
        public void Translate(Block[] palette)
        {
            for (int i = 0; i < palette.Length; i++)
            {
                palette[i] = Translate(palette[i]);
            }
        }

        // Matches the table's key format: name[a=1,b=2] with properties sorted by name
        private static string StateKey(Block block)
        {
            if (block.Properties.Length == 0) return block.Name + "[]";
            return $"{block.Name}[{string.Join(",", block.Properties.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"{p.Name}={p.Value}"))}]";
        }
        private static string NameOf(string state)
        {
            int bracket = state.IndexOf('[');
            return bracket < 0 ? state : state[..bracket];
        }
        public class LegacyTable
        {
            // Old state key -> Java state; "name@value" -> Java state; numeric ID -> name
            public Dictionary<string, string> States { get; init; } = [];
            public Dictionary<string, string> Values { get; init; } = [];
            public Dictionary<string, string> Ids { get; init; } = [];

            // Old block name -> the Java name its states mostly become, for states the table doesn't list
            public Dictionary<string, string> Names { get; } = [];

            public static LegacyTable FromFile(string file)
            {
                LegacyTable table = JsonSerializer.Deserialize<LegacyTable>(File.ReadAllText(file)) ?? new LegacyTable();
                foreach (var state in table.States)
                {
                    table.Names.TryAdd(NameOf(state.Key), NameOf(state.Value));
                }
                return table;
            }
        }

        private static Block ParseState(string state)
        {
            int bracket = state.IndexOf('[');
            if (bracket < 0) return new Block(state);

            string inner = state[(bracket + 1)..^1];
            if (inner.Length == 0) return new Block(state[..bracket]);

            Property[] properties = inner.Split(',').Select(pair =>
            {
                int equals = pair.IndexOf('=');
                return new Property(pair[..equals], pair[(equals + 1)..]);
            }).ToArray();

            return new Block(state[..bracket], properties);
        }
    }
}
