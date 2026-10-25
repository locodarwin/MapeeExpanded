namespace Mapee.Tests.Fixtures
{
    // A small Java world with something to look at and something to find: 12 by 12 chunks of stone under grass,
    // a sea along one side, diamond ore deep down, a spawner and a chest, a village saved as starting in the chunk
    // at 2, 2 (though none is built) and a sign that says something. Made up rather than played, so it can
    // go into CI, where the Windows smoke test opens it (the sample worlds are private)
    public static class SmokeWorld
    {
        public const int Radius = 6;
        public const string Name = "Smoke test world";

        public static JavaTestWorld Write(string? directory = null)
        {
            JavaTestWorld world = new(directory) { Keep = directory is not null };
            for (int x = -Radius; x < Radius; x++)
            {
                for (int z = -Radius; z < Radius; z++) world.Chunk(x, z, Chunk(x, z));
            }
            return world.Save(Name);
        }

        private static Dictionary<string, object> State(string name) => new() { ["Name"] = "minecraft:" + name };

        private static Dictionary<string, object> Section(int y, string biome, List<object> palette, Func<int, int, int, int> indexAt) => new()
        {
            ["Y"] = unchecked((byte)(sbyte)y),
            ["block_states"] = palette.Count == 1
                ? new Dictionary<string, object> { ["palette"] = palette }
                : new Dictionary<string, object> { ["palette"] = palette, ["data"] = JavaTestWorld.PackBlockStates(palette.Count, indexAt) },
            ["biomes"] = new Dictionary<string, object> { ["palette"] = new List<object> { "minecraft:" + biome } }
        };

        // Two pieces, which together cover blocks 24 to 55 sideways
        public static readonly (int X, int Y, int Z) VillageMiddle = (39, 66, 39);
        private static Dictionary<string, object> Village => new()
        {
            ["starts"] = new Dictionary<string, object>
            {
                ["minecraft:village_plains"] = new Dictionary<string, object>
                {
                    ["id"] = "minecraft:village_plains",
                    ["ChunkX"] = 2,
                    ["ChunkZ"] = 2,
                    ["Children"] = new List<object>
                    {
                        new Dictionary<string, object> { ["id"] = "minecraft:jigsaw", ["BB"] = new[] { 32, 63, 32, 47, 70, 47 } },
                        new Dictionary<string, object> { ["id"] = "minecraft:jigsaw", ["BB"] = new[] { 24, 64, 30, 55, 69, 48 } }
                    }
                }
            },
            ["References"] = new Dictionary<string, object>()
        };

        // A sign standing on the grass, as 1.20 and later save one: each line JSON text, and a back
        public static readonly (int X, int Y, int Z) SignAt = (20, 64, 20);
        public static readonly string[] SignFront = ["Welcome to", "the smoke test"];
        private static Dictionary<string, object> Sign => new()
        {
            ["id"] = "minecraft:sign",
            ["x"] = SignAt.X,
            ["y"] = SignAt.Y,
            ["z"] = SignAt.Z,
            ["keepPacked"] = (byte)0,
            ["is_waxed"] = (byte)0,
            ["front_text"] = new Dictionary<string, object>
            {
                ["messages"] = new List<object> { "{\"text\":\"Welcome to\"}", "{\"text\":\"the smoke test\"}", "\"\"", "\"\"" },
                ["color"] = "black",
                ["has_glowing_text"] = (byte)0
            },
            ["back_text"] = new Dictionary<string, object>
            {
                ["messages"] = new List<object> { "\"\"", "\"\"", "\"\"", "\"\"" },
                ["color"] = "black",
                ["has_glowing_text"] = (byte)0
            }
        };

        private static Dictionary<string, object> Chunk(int x, int z)
        {
            bool sea = x <= -3;
            string biome = sea ? "ocean" : "plains";
            List<object> sections = [Section(-4, biome, [State("bedrock")], (bx, by, bz) => 0)];

            // Stone from Y -48 to 47, with diamond ore scattered through the lowest section
            sections.Add(Section(-3, biome, [State("stone"), State("diamond_ore")], (bx, by, bz) => (bx * 7 + bz * 3 + by * 5 + x * 11 + z * 13) % 89 == 0 ? 1 : 0));
            for (int y = -2; y <= 2; y++) sections.Add(Section(y, biome, [State("stone")], (bx, by, bz) => 0));

            // Y 48 to 63: stone, then grass on top, or sand under the sea; the chunk at 0, 0 holds a spawner at Y 50
            // and the one beside it a chest
            sections.Add(Section(3, biome, [State("stone"), State(sea ? "sand" : "grass_block"), State("spawner"), State("chest")], (bx, by, bz) =>
                by == 15 ? 1 : x == 0 && z == 0 && bx == 8 && bz == 8 && by == 2 ? 2 : x == 1 && z == 0 && bx == 4 && bz == 4 && by == 2 ? 3 : 0));
            // Y 64 to 79: open air, or a sea up to Y 70
            sections.Add(Section(4, biome, [State("air"), State("water")], (bx, by, bz) => sea && by <= 6 ? 1 : 0));

            Dictionary<string, object> output = new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = sections
            };
            if (x == 2 && z == 2) output["structures"] = Village;
            if (x == SignAt.X >> 4 && z == SignAt.Z >> 4) output["block_entities"] = new List<object> { Sign };
            return output;
        }
    }
}
