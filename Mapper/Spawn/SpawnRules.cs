using System.Collections.Concurrent;
using WorldEditor;

namespace Mapper
{
    // What a block is to a hostile mob looking for somewhere to spawn
    public enum SpawnKind : byte
    {
        // Room to stand in: air, plants, torches, rails, signs, snow
        Open,
        Water,
        // Mobs can spawn on top of it
        Floor,
        // In the way, but nothing hostile spawns on it: glass, leaves, ice, bottom slabs, carpets, fences, chests
        Ledge
    }

    // Where hostile mobs can spawn, as the game has it since 1.18 in both editions: on top of a solid block with room
    // above, where the block light is 0. Sky light is left out, since at night it doesn't stop them. The lists are of
    // the names the map uses (Java's, which Bedrock blocks are translated to); a block they don't name is a floor
    public static class SpawnRules
    {
        private static readonly HashSet<string> OpenBlocks = [
            "air", "cave_air", "void_air", "short_grass", "grass", "tall_grass", "fern", "large_fern", "dead_bush", "bush",
            "firefly_bush", "short_dry_grass", "tall_dry_grass", "sugar_cane", "vine", "glow_lichen", "sculk_vein",
            "hanging_roots", "pale_hanging_moss", "spore_blossom", "nether_sprouts", "crimson_roots", "warped_roots",
            "crimson_fungus", "warped_fungus", "brown_mushroom", "red_mushroom", "nether_wart", "wheat", "carrots",
            "potatoes", "beetroots", "melon_stem", "pumpkin_stem", "attached_melon_stem", "attached_pumpkin_stem",
            "torchflower_crop", "pitcher_crop", "sweet_berry_bush", "twisting_vines", "twisting_vines_plant",
            "weeping_vines", "weeping_vines_plant", "cave_vines", "cave_vines_plant", "small_dripleaf", "big_dripleaf_stem",
            "redstone_wire", "rail", "lever", "tripwire", "tripwire_hook", "torch", "snow", "cobweb", "light",
            "structure_void", "fire", "soul_fire", "nether_portal", "end_portal", "end_gateway", "pink_petals",
            "wildflowers", "leaf_litter", "cactus_flower", "resin_clump", "lily_pad", "frogspawn",
            "dandelion", "poppy", "blue_orchid", "allium", "azure_bluet", "red_tulip", "orange_tulip", "white_tulip",
            "pink_tulip", "oxeye_daisy", "cornflower", "lily_of_the_valley", "wither_rose", "torchflower", "sunflower",
            "lilac", "rose_bush", "peony", "pitcher_plant", "open_eyeblossom", "closed_eyeblossom"
        ];
        private static readonly string[] OpenEndings = ["_sapling", "_torch", "_button", "_sign", "_banner", "_pressure_plate", "_rail", "_coral", "_coral_fan"];

        private static readonly HashSet<string> WaterBlocks = ["water", "lava", "bubble_column", "kelp", "kelp_plant", "seagrass", "tall_seagrass"];

        private static readonly HashSet<string> LedgeBlocks = [
            "glass", "glass_pane", "iron_bars", "chain", "ice", "packed_ice", "blue_ice", "frosted_ice", "magma_block",
            "barrier", "farmland", "dirt_path", "honey_block", "cactus", "bamboo", "scaffolding", "end_rod", "lightning_rod",
            "pointed_dripstone", "chorus_plant", "chorus_flower", "conduit", "sea_pickle", "turtle_egg", "sniffer_egg",
            "decorated_pot", "dragon_egg", "end_portal_frame", "azalea", "flowering_azalea", "big_dripleaf", "cocoa",
            "amethyst_cluster", "heavy_core", "vault", "spawner", "trial_spawner", "beacon", "enchanting_table",
            "brewing_stand", "cauldron", "hopper", "lectern", "stonecutter", "grindstone", "bell", "daylight_detector",
            "repeater", "comparator", "composter", "cake", "candle", "flower_pot", "anvil", "lantern", "campfire",
            "soul_campfire", "chest", "trapped_chest", "ender_chest", "shulker_box", "powder_snow", "mangrove_roots",
            "piston_head", "moving_piston", "sculk_sensor", "calibrated_sculk_sensor", "sculk_shrieker", "creaking_heart_top",
            "copper_grate"
        ];
        private static readonly string[] LedgeEndings = ["glass", "_pane", "leaves", "_carpet", "_fence", "_fence_gate", "_wall", "_bed", "_door",
            "_trapdoor", "_shulker_box", "_head", "_skull", "_candle", "_cake", "_anvil", "_lantern", "_chain", "_bars", "_chest",
            "_cauldron", "_amethyst_bud", "_lightning_rod", "_grate", "_campfire"];
        private static readonly string[] LedgeStarts = ["potted_"];

        // Hostile mobs don't spawn in these, however dark
        private static readonly HashSet<string> NoSpawnBiomes = ["minecraft:mushroom_fields", "minecraft:deep_dark"];

        private static readonly ConcurrentDictionary<string, SpawnKind> _byName = new();

        // The Nether's mobs spawn whatever the light, so there is nothing to light up there
        public static bool HasSpawning(Dimension dimension) => dimension == Dimension.Overworld || dimension == Dimension.TheEnd;

        public static bool IsSafeBiome(string? biome) => biome is not null && NoSpawnBiomes.Contains(biome);

        public static SpawnKind Kind(Block block)
        {
            string name = block.Name ?? string.Empty;
            SpawnKind kind = _byName.GetOrAdd(name, ByName);

            // A slab on the top half, or a double one, and stairs upside down have a full top
            if (kind == SpawnKind.Ledge && (name.EndsWith("_slab") || name.EndsWith("_stairs")))
            {
                foreach (Property property in block.Properties)
                {
                    if (property.Name == "type" && property.Value is "top" or "double") return SpawnKind.Floor;
                    if (property.Name == "half" && property.Value == "top") return SpawnKind.Floor;
                }
            }
            // Room to stand in, unless it's full of water
            if (kind == SpawnKind.Open)
            {
                foreach (Property property in block.Properties)
                {
                    if (property.Name == "waterlogged" && property.Value == "true") return SpawnKind.Water;
                }
            }
            return kind;
        }

        private static SpawnKind ByName(string fullName)
        {
            if (fullName.Length == 0) return SpawnKind.Open;
            string name = fullName.StartsWith("minecraft:") ? fullName["minecraft:".Length..] : fullName;

            if (WaterBlocks.Contains(name)) return SpawnKind.Water;
            if (OpenBlocks.Contains(name) || OpenEndings.Any(name.EndsWith)) return SpawnKind.Open;
            if (LedgeBlocks.Contains(name) || LedgeEndings.Any(name.EndsWith) || LedgeStarts.Any(name.StartsWith)) return SpawnKind.Ledge;
            if (name.EndsWith("_slab") || name.EndsWith("_stairs")) return SpawnKind.Ledge;
            return SpawnKind.Floor;
        }
    }
}
