using WorldEditor;

namespace Mapee.Tests.Fixtures
{
    public static class ChunkInspection
    {
        public static Block? BlockAt(BlockStateChunk chunk, int x, int y, int z)
        {
            PaletteSection<Block>? section = chunk.Sections.FirstOrDefault(s => s.Y == y >> 4);
            if (section is null) return null;

            short[] indexes = new short[4096];
            section.Unlock(indexes);
            return section.Palette[indexes[(y & 15) << 8 | z << 4 | x]];
        }

        public static string? NameAt(BlockStateChunk chunk, int x, int y, int z) => BlockAt(chunk, x, y, z)?.Name;

        public static short HeightAt(HeightmapCollection heightmaps, int x, int z)
        {
            short[] heights = new short[256];
            heightmaps.Heightmaps["WORLD_SURFACE"].Unlock(heights);
            return heights[z * 16 + x];
        }

        // Biome cells are 4x4x4; returns the cell holding the block
        public static string BiomeAt(BiomeChunk biomes, int x, int y, int z)
        {
            PaletteSection<string> section = biomes.Sections.First(s => s.Y == y >> 4);
            if (section.Palette.Length == 1) return section.Palette[0];

            short[] cells = new short[64];
            section.Unlock(cells);
            return section.Palette[cells[((y & 15) >> 2) * 16 + (z >> 2) * 4 + (x >> 2)]];
        }

        public static Dictionary<byte[], byte[]> Records(params (byte[] Key, byte[] Value)[] records)
        {
            Dictionary<byte[], byte[]> output = new(ByteArrayComparer.Instance);
            foreach (var (key, value) in records) output[key] = value;
            return output;
        }
    }
}
