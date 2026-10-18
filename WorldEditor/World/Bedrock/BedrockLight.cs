namespace WorldEditor
{
    // Gives a chunk read from another Bedrock chunk's light computation access to its neighbours
    public interface IBedrockNeighborSource
    {
        // Bit n set means section (lowest + n) holds a block that gives off light
        ulong EmitterMask(int x, int z);
        void SetEmitterMask(int x, int z, ulong mask);
        BlockStateChunk? ReadBlocks(int x, int z);
    }

    // Bedrock doesn't save light, but Mapee shades blocks by it: sky light for anything under water, glass or
    // an overhang, block light for night mode and cave views. Both are computed here from Java's light values
    public static class BedrockLight
    {
        private const int MARGIN = 15;
        private const int WIDTH = 16 + 2 * MARGIN;

        public static BedrockLightTable Table => BedrockLightTable.Default;

        public static ulong EmitterMask(BlockStateChunk blocks, int dimension)
        {
            sbyte lowest = BedrockDimension.LowestSection(dimension);
            ulong mask = 0;
            foreach (PaletteSection<Block> section in blocks.Sections)
            {
                int bit = section.Y - lowest;
                if (bit < 0 || bit > 63) continue;
                if (section.Palette.Any(block => Table.Emission(block) > 0)) mask |= 1UL << bit;
            }
            return mask;
        }

        // Sky light falls straight down each column, dimmed by each block's filter, then spreads sideways and
        // down like block light, which lights cave entrances, overhangs and the ground under trees. It's worked
        // out in the same box as block light, reaching 15 blocks into the neighbours. Layers lit fully across the
        // box can't change, so the box starts at the first one that isn't, and ends one section below where
        // falling light runs out
        public static LightChunk SkyLight(int x, int z, int dimension, BlockStateChunk own, IBedrockNeighborSource? neighbors)
        {
            LightChunk output = new();
            if (own.Sections.Count < 1) return output;

            const int layer = WIDTH * WIDTH, slab = layer * 16;
            sbyte bottom = BedrockDimension.LowestSection(dimension);
            sbyte ownTop = own.Sections.Max(s => s.Y);

            // Columns of a missing neighbour never see the sky
            byte[] column = new byte[layer];
            Array.Fill(column, (byte)15);
            Dictionary<int, PaletteSection<Block>>?[,] chunks = new Dictionary<int, PaletteSection<Block>>?[3, 3];
            sbyte top = ownTop;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    BlockStateChunk? blocks = dx == 0 && dz == 0 ? own : neighbors?.ReadBlocks(x + dx, z + dz);
                    if (blocks is null || blocks.Sections.Count < 1)
                    {
                        ForWindow(dx, dz, (cell, bx, bz) => column[cell] = 0);
                        continue;
                    }

                    chunks[dx + 1, dz + 1] = blocks.Sections.ToDictionary(s => (int)s.Y);
                    top = Math.Max(top, blocks.Sections.Max(s => s.Y));
                }
            }

            List<(byte[] Filter, byte[] Light)> slabs = [];
            sbyte start = 0;
            short[] indexes = new short[4096];
            for (sbyte y = top; y >= bottom; y--)
            {
                byte[] filter = new byte[slab], light = new byte[slab];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Dictionary<int, PaletteSection<Block>>? sections = chunks[dx + 1, dz + 1];
                        if (sections is null)
                        {
                            for (int by = 0; by < 16; by++) ForWindow(dx, dz, (cell, bx, bz) => filter[by * layer + cell] = 15);
                            continue;
                        }

                        PaletteSection<Block>? section = sections.GetValueOrDefault(y);
                        if (section is null) continue;

                        byte[] sectionFilter = section.Palette.Select(Table.Filter).ToArray();
                        Unlock(section, indexes);
                        for (int by = 0; by < 16; by++)
                        {
                            ForWindow(dx, dz, (cell, bx, bz) => filter[by * layer + cell] = sectionFilter[indexes[by << 8 | bz << 4 | bx]]);
                        }
                    }
                }

                bool full = true, anyLit = false;
                for (int by = 15; by >= 0; by--)
                {
                    for (int i = 0; i < layer; i++)
                    {
                        int cell = by * layer + i;
                        byte value = (byte)Math.Max(0, column[i] - filter[cell]);
                        column[i] = value;
                        light[cell] = value;

                        full &= value == 15;
                        anyLit |= value > 0;
                    }
                }

                if (slabs.Count == 0)
                {
                    if (full) continue;
                    start = y;
                }
                slabs.Add((filter, light));
                if (!anyLit) break;
            }

            // The box, bottom up, from the slabs collected top down
            int height = slabs.Count * 16;
            byte[] filterBox = new byte[slabs.Count * slab], lightBox = new byte[filterBox.Length];
            for (int k = 0; k < slabs.Count; k++)
            {
                int offset = (slabs.Count - 1 - k) * slab;
                slabs[k].Filter.CopyTo(filterBox, offset);
                slabs[k].Light.CopyTo(lightBox, offset);
            }

            Spread(lightBox, filterBox, Seeds(lightBox, filterBox, height), height);

            int lowestY = start - slabs.Count + 1;
            for (int y = ownTop + 1; y >= lowestY && y >= bottom; y--)
            {
                if (slabs.Count == 0 || y > start)
                {
                    output.Sections.Add(Uniform((sbyte)y, 15));
                    continue;
                }

                LightChunk.Section? section = ExtractSection(lightBox, y - lowestY, (sbyte)y);
                if (section is not null) output.Sections.Add(section);
            }

            return output;
        }

        // Lit cells next to a cell they could brighten; spreading from only these does the same work as from all
        private static List<int> Seeds(byte[] light, byte[] filter, int height)
        {
            const int layer = WIDTH * WIDTH;
            List<int> seeds = [];
            for (int cell = 0; cell < light.Length; cell++)
            {
                int level = light[cell];
                if (level < 2) continue;

                int x = cell % WIDTH, z = cell / WIDTH % WIDTH, y = cell / layer;
                if ((x > 0 && Brightens(cell - 1)) || (x < WIDTH - 1 && Brightens(cell + 1)) ||
                    (z > 0 && Brightens(cell - WIDTH)) || (z < WIDTH - 1 && Brightens(cell + WIDTH)) ||
                    (y > 0 && Brightens(cell - layer)) || (y < height - 1 && Brightens(cell + layer)))
                {
                    seeds.Add(cell);
                }

                bool Brightens(int next) => level - Math.Max(1, (int)filter[next]) > light[next];
            }
            return seeds;
        }

        // Calls body for each cell of one layer of the box that lies inside the chunk at (dx, dz)
        private static void ForWindow(int dx, int dz, Action<int, int, int> body)
        {
            int minX = dx < 0 ? 16 - MARGIN : 0, maxX = dx > 0 ? MARGIN - 1 : 15;
            int minZ = dz < 0 ? 16 - MARGIN : 0, maxZ = dz > 0 ? MARGIN - 1 : 15;
            for (int bz = minZ; bz <= maxZ; bz++)
            {
                int row = (dz * 16 + bz + MARGIN) * WIDTH + dx * 16 + MARGIN;
                for (int bx = minX; bx <= maxX; bx++) body(row + bx, bx, bz);
            }
        }

        // Block light spreads from every light source, losing at least one level per block. A chunk is lit
        // inside a box reaching 15 blocks into all eight neighbours, so light crosses chunk borders as in Java.
        // Only the sections near a light source are worked on; a chunk with none nearby gets no block light
        public static LightChunk? BlockLight(int x, int z, int dimension, BlockStateChunk own, ulong ownMask, IBedrockNeighborSource? neighbors)
        {
            ulong band = Spread(ownMask);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx != 0 || dz != 0) band |= Spread(neighbors?.EmitterMask(x + dx, z + dz) ?? 0);
                }
            }
            if (band == 0) return null;

            // Runs of sections with no light source within reach between them can't light each other,
            // so each run is lit on its own
            LightChunk output = new();
            int lo = 0;
            while (lo < 64 && band >> lo != 0)
            {
                lo += System.Numerics.BitOperations.TrailingZeroCount(band >> lo);
                int hi = lo + System.Numerics.BitOperations.TrailingZeroCount(~(band >> lo)) - 1;
                foreach (LightChunk.Section section in LightRun(x, z, dimension, own, neighbors, lo, hi)) output.Sections.Add(section);
                lo = hi + 1;
            }
            return output.Sections.Count > 0 ? output : null;
        }

        private static List<LightChunk.Section> LightRun(int x, int z, int dimension, BlockStateChunk own, IBedrockNeighborSource? neighbors, int lo, int hi)
        {
            int height = (hi - lo + 1) * 16;
            sbyte lowest = BedrockDimension.LowestSection(dimension);

            // Cells outside any chunk that was read stay solid, so light doesn't leak into unknown space
            byte[] filter = new byte[WIDTH * WIDTH * height];
            byte[] light = new byte[filter.Length];
            Array.Fill(filter, (byte)15);
            List<int> queue = [];
            short[] indexes = new short[4096];

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    BlockStateChunk? blocks;
                    if (dx == 0 && dz == 0) blocks = own;
                    else blocks = neighbors?.ReadBlocks(x + dx, z + dz);
                    if (blocks is null) continue;

                    Dictionary<int, PaletteSection<Block>> sections = blocks.Sections.ToDictionary(s => s.Y - lowest);
                    int minX = dx < 0 ? 16 - MARGIN : 0, maxX = dx > 0 ? MARGIN - 1 : 15;
                    int minZ = dz < 0 ? 16 - MARGIN : 0, maxZ = dz > 0 ? MARGIN - 1 : 15;

                    for (int s = lo; s <= hi; s++)
                    {
                        PaletteSection<Block>? section = sections.GetValueOrDefault(s);
                        byte[] sectionFilter = section?.Palette.Select(Table.Filter).ToArray() ?? [0];
                        byte[] sectionEmit = section?.Palette.Select(Table.Emission).ToArray() ?? [0];
                        if (section is null) Array.Clear(indexes);
                        else Unlock(section, indexes);

                        for (int by = 0; by < 16; by++)
                        {
                            int vy = (s - lo) * 16 + by;
                            for (int bz = minZ; bz <= maxZ; bz++)
                            {
                                int row = (vy * WIDTH + dz * 16 + bz + MARGIN) * WIDTH + dx * 16 + MARGIN;
                                for (int bx = minX; bx <= maxX; bx++)
                                {
                                    int palette = indexes[by << 8 | bz << 4 | bx];
                                    int cell = row + bx;
                                    filter[cell] = sectionFilter[palette];

                                    byte emit = sectionEmit[palette];
                                    if (emit > light[cell])
                                    {
                                        light[cell] = emit;
                                        queue.Add(cell);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Spread(light, filter, queue, height);
            return Extract(light, lo, lowest, height);
        }

        private static void Spread(byte[] light, byte[] filter, List<int> queue, int height)
        {
            int layer = WIDTH * WIDTH;
            for (int head = 0; head < queue.Count; head++)
            {
                int cell = queue[head];
                int level = light[cell];
                if (level <= 1) continue;

                int x = cell % WIDTH, z = cell / WIDTH % WIDTH, y = cell / layer;
                if (x > 0) Visit(cell - 1);
                if (x < WIDTH - 1) Visit(cell + 1);
                if (z > 0) Visit(cell - WIDTH);
                if (z < WIDTH - 1) Visit(cell + WIDTH);
                if (y > 0) Visit(cell - layer);
                if (y < height - 1) Visit(cell + layer);

                void Visit(int next)
                {
                    int value = level - Math.Max(1, (int)filter[next]);
                    if (value > light[next])
                    {
                        light[next] = (byte)value;
                        queue.Add(next);
                    }
                }
            }
        }

        private static List<LightChunk.Section> Extract(byte[] light, int lo, sbyte lowest, int height)
        {
            List<LightChunk.Section> output = [];
            for (int s = 0; s < height / 16; s++)
            {
                LightChunk.Section? section = ExtractSection(light, s, (sbyte)(lowest + lo + s));
                if (section is not null) output.Add(section);
            }
            return output;
        }

        // The chunk's own 16x16 columns of one section of the box, or null if that section is dark
        private static LightChunk.Section? ExtractSection(byte[] light, int boxSection, sbyte y)
        {
            LightChunk.Section section = new(new byte[2048]) { Y = y };
            bool anyLight = false;
            for (int by = 0; by < 16; by++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int row = ((boxSection * 16 + by) * WIDTH + bz + MARGIN) * WIDTH + MARGIN;
                    for (int bx = 0; bx < 16; bx++)
                    {
                        byte value = light[row + bx];
                        if (value == 0) continue;

                        section.SetLight(by << 8 | bz << 4 | bx, value);
                        anyLight = true;
                    }
                }
            }
            return anyLight ? section : null;
        }

        // A light source reaches at most 15 blocks, so it can only light its own section and the ones beside it
        private static ulong Spread(ulong mask)
        {
            return mask | mask << 1 | mask >> 1;
        }

        private static void Unlock(PaletteSection<Block> section, short[] indexes)
        {
            if (section.Type == SectionType.Uniform) Array.Clear(indexes);
            else section.Unlock(indexes);
        }

        private static LightChunk.Section Uniform(sbyte y, byte value)
        {
            byte[] values = new byte[2048];
            Array.Fill(values, (byte)(value | value << 4));
            return new LightChunk.Section(values) { Y = y };
        }
    }
}
