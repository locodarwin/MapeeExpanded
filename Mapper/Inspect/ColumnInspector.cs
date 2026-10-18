using WorldEditor;

namespace Mapper
{
    // What one column of the map shows: the block seen from above, its height and biome, and for water or
    // lava, how deep it is and what lies at the bottom
    public class InspectedColumn
    {
        public Block Block { get; init; }
        public int Y { get; init; }
        public string? Biome { get; init; }

        public int Depth { get; init; }
        public Block? Floor { get; init; }
        public int FloorY { get; init; }
    }

    public static class ColumnInspector
    {
        private static readonly HashSet<string> Air = ["minecraft:air", "minecraft:cave_air", "minecraft:void_air"];
        private static readonly HashSet<string> Liquids = ["minecraft:water", "minecraft:lava", "minecraft:bubble_column",
            "minecraft:kelp", "minecraft:kelp_plant", "minecraft:seagrass", "minecraft:tall_seagrass"];

        // Looks down from startY (the top of the chunk if null). In cave mode, as the cave scan draws it, the
        // solid blocks above the first air are skipped and the first block below that air is the one shown
        public static InspectedColumn? Inspect(ConvertedApiChunk chunk, int x, int z, int? startY = null, bool caves = false)
        {
            if (chunk.BlockState is null || chunk.BlockState.Sections.Count == 0) return null;

            Column column = new(chunk);
            int y = Math.Min(startY ?? int.MaxValue, column.Top);
            bool passedAir = !caves;

            for (; y >= column.Bottom; y--)
            {
                bool isAir = column.GetBlock(x, y, z) is not Block block || Air.Contains(block.Name);
                if (isAir)
                {
                    passedAir = true;
                    continue;
                }
                if (passedAir) break;
            }
            if (y < column.Bottom || column.GetBlock(x, y, z) is not Block top) return null;

            int depth = 0, floorY = y;
            Block? floor = null;
            if (Liquids.Contains(top.Name))
            {
                for (floorY = y; floorY >= column.Bottom; floorY--)
                {
                    Block? below = column.GetBlock(x, floorY, z);
                    if (below is Block b && !Liquids.Contains(b.Name) && !Air.Contains(b.Name))
                    {
                        floor = b;
                        break;
                    }
                    depth++;
                }
            }

            return new InspectedColumn()
            {
                Block = top,
                Y = y,
                Biome = column.GetBiome(x, y, z),
                Depth = depth,
                Floor = floor,
                FloorY = floorY
            };
        }

        // "minecraft:grass_block" reads as "Grass Block"; other namespaces keep theirs
        public static string DisplayName(string name)
        {
            string prefix = string.Empty;
            int colon = name.IndexOf(':');
            if (colon >= 0)
            {
                if (name[..colon] != "minecraft") prefix = name[..(colon + 1)];
                name = name[(colon + 1)..];
            }

            string[] words = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return prefix + string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        }

        // Sections unpacked as they're reached; a missing section is air
        private class Column
        {
            public int Top { get; }
            public int Bottom { get; }

            private readonly Dictionary<int, PaletteSection<Block>> _blocks;
            private readonly Dictionary<int, PaletteSection<string>> _biomes;
            private readonly Dictionary<object, short[]> _unlocked = new(ReferenceEqualityComparer.Instance);

            public Column(ConvertedApiChunk chunk)
            {
                _blocks = chunk.BlockState!.Sections.ToDictionary(s => (int)s.Y);
                _biomes = chunk.Biome?.Sections.ToDictionary(s => (int)s.Y) ?? [];

                Top = _blocks.Keys.Max() * 16 + 15;
                Bottom = _blocks.Keys.Min() * 16;
            }

            public Block? GetBlock(int x, int y, int z)
            {
                if (!_blocks.TryGetValue(MathUtilities.FindSectionY(y), out PaletteSection<Block>? section)) return null;
                return section.Palette[Index(section, MathUtilities.FindBlockIndex(x, y, z))];
            }
            public string? GetBiome(int x, int y, int z)
            {
                if (!_biomes.TryGetValue(MathUtilities.FindSectionY(y), out PaletteSection<string>? section)) return null;
                return section.Palette[Index(section, MathUtilities.FindBiomeIndex(x, y, z))];
            }

            private short Index<T>(PaletteSection<T> section, int index)
            {
                if (section.Type == SectionType.Uniform || section.Locker is null) return 0;

                if (!_unlocked.TryGetValue(section, out short[]? indexes))
                {
                    indexes = new short[section.Locker.UnlockedArrayLength];
                    section.Unlock(indexes);
                    _unlocked[section] = indexes;
                }
                return indexes[index];
            }
        }
    }
}
