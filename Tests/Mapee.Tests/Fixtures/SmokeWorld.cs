namespace Mapee.Tests.Fixtures
{
    // A small Java world with something to look at and something to find: 12 by 12 chunks of stone under grass,
    // a sea along one side, diamond ore deep down, and a spawner and a chest. Made up rather than played, so it can
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

            return new()
            {
                ["DataVersion"] = JavaTestWorld.DataVersion,
                ["xPos"] = x,
                ["zPos"] = z,
                ["yPos"] = -4,
                ["Status"] = "minecraft:full",
                ["sections"] = sections
            };
        }
    }
}
