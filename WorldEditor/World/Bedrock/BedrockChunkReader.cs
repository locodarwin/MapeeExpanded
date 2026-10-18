namespace WorldEditor
{
    // Builds the same ApiChunk a Java chunk produces, from one chunk's Bedrock records.
    // Bedrock doesn't save light, so sky and block light are computed (see BedrockLight).
    // Chunks last saved before 1.18 keep biomes per column in Data2D, and before 1.0 the whole chunk in LegacyTerrain
    public class BedrockChunkReader
    {
        public BedrockSubChunkReader SubChunkReader { get; set; } = new();
        public BedrockData3DReader Data3DReader { get; set; } = new();

        // The default table is a few megabytes, so it's loaded on the first chunk rather than when the reader is made
        public BedrockBlockTranslator BlockTranslator
        {
            get => _blockTranslator ??= BedrockBlockTranslator.Default;
            set => _blockTranslator = value;
        }
        private BedrockBlockTranslator? _blockTranslator;

        public bool RemoveEmptyAirSections { get; set; } = true;
        public bool ComputeSkyLight { get; set; } = true;
        public bool ComputeBlockLight { get; set; } = true;

        // blocks, if given, are this chunk's already-decoded blocks; records then only supply Data3D
        public ApiChunk? Read(int x, int z, int dimension, IDictionary<byte[], byte[]> records, IBedrockNeighborSource? neighbors = null, BlockStateChunk? blocks = null)
        {
            byte[]? data3D;
            BlockStateChunk? blockState;
            if (blocks is null)
            {
                blockState = ReadBlocks(x, z, dimension, records, out data3D);
            }
            else
            {
                // The cached blocks are shared with neighbours' light, so the chunk handed to Mapee gets its own
                // list and palettes: Mapee clears the list once drawn, and its block renamer edits palettes in place
                blockState = new BlockStateChunk(blocks.Sections.Count);
                foreach (PaletteSection<Block> section in blocks.Sections)
                {
                    blockState.Sections.Add(new PaletteSection<Block>((Block[])section.Palette.Clone(), section.Indexes)
                    {
                        Y = section.Y,
                        Locker = section.Locker
                    });
                }
                data3D = FindRecord(records, x, z, dimension, BedrockChunkTag.Data3D);
            }
            if (blockState is null) return null;

            ApiChunk output = new()
            {
                X = x,
                Z = z,
                Version = Version.Newest,
                BlockState = blockState,
                SkyLight = ComputeSkyLight ? BedrockLight.SkyLight(x, z, dimension, blockState, neighbors) : null
            };

            if (ComputeBlockLight)
            {
                ulong mask = BedrockLight.EmitterMask(blockState, dimension);
                neighbors?.SetEmitterMask(x, z, mask);
                output.BlockLight = BedrockLight.BlockLight(x, z, dimension, blockState, mask, neighbors);
            }

            if (data3D is not null && data3D.Length >= 512)
            {
                output.Heightmap = Data3DReader.ReadHeightmap(data3D, dimension);
                output.Biome = Data3DReader.ReadBiomes(data3D, dimension);
            }
            else
            {
                ReadColumns(x, z, dimension, records, blockState, output);
            }

            return output;
        }

        // Heightmap and biomes of chunks older than Data3D. Their heights count from 0, the floor at the time
        private void ReadColumns(int x, int z, int dimension, IDictionary<byte[], byte[]> records, BlockStateChunk blockState, ApiChunk output)
        {
            IEnumerable<sbyte> sectionYs = blockState.Sections.Select(s => s.Y);

            if (FindRecord(records, x, z, dimension, BedrockChunkTag.Data2D) is byte[] data2D && data2D.Length >= 512 + 256)
            {
                output.Heightmap = Data3DReader.ReadColumnHeightmap(data2D);
                output.Biome = Data3DReader.ReadColumnBiomes(data2D.AsSpan(512, 256), 1, sectionYs);
            }
            else if (FindRecord(records, x, z, dimension, BedrockChunkTag.Data2DLegacy) is byte[] legacy2D && legacy2D.Length >= 512 + 1024)
            {
                output.Heightmap = Data3DReader.ReadColumnHeightmap(legacy2D);
                output.Biome = Data3DReader.ReadColumnBiomes(legacy2D.AsSpan(512, 1024), 4, sectionYs);
            }
            else if (FindRecord(records, x, z, dimension, BedrockChunkTag.LegacyTerrain) is byte[] terrain && terrain.Length >= LEGACY_TERRAIN_LENGTH)
            {
                output.Heightmap = Data3DReader.ReadColumnHeightmap(LegacyTerrainHeights(terrain));
                output.Biome = Data3DReader.ReadColumnBiomes(terrain.AsSpan(LEGACY_TERRAIN_BIOMES, 1024), 4, sectionYs);
            }
        }

        private static byte[]? FindRecord(IDictionary<byte[], byte[]> records, int x, int z, int dimension, BedrockChunkTag tag)
        {
            foreach (var record in records)
            {
                if (BedrockKey.TryParse(record.Key, out BedrockKey key) && key.Tag == tag && key.Dimension == dimension && key.X == x && key.Z == z) return record.Value;
            }
            return null;
        }

        // LegacyTerrain (before 1.0): 16x16x128 block IDs in XZY order, data values, sky light and block light as
        // nibbles, a heightmap and per-column biome ID + colour
        private const int LEGACY_TERRAIN_BLOCKS = 16 * 16 * 128;
        private const int LEGACY_TERRAIN_BIOMES = LEGACY_TERRAIN_BLOCKS + LEGACY_TERRAIN_BLOCKS / 2 * 3 + 256;
        private const int LEGACY_TERRAIN_LENGTH = LEGACY_TERRAIN_BIOMES + 1024;

        private static IEnumerable<PaletteSection<Block>> ReadLegacyTerrain(byte[] terrain)
        {
            byte[] ids = new byte[4096];
            byte[] data = new byte[2048];
            for (int sy = 0; sy < 8; sy++)
            {
                for (int column = 0; column < 256; column++)
                {
                    // column is x * 16 + z in both layouts; only the height differs
                    Array.Copy(terrain, column * 128 + sy * 16, ids, column * 16, 16);
                    Array.Copy(terrain, LEGACY_TERRAIN_BLOCKS + (column * 128 + sy * 16) / 2, data, column * 8, 8);
                }
                yield return BedrockSubChunkReader.ReadNumeric(ids, data, (sbyte)sy);
            }
        }

        // The stored heightmap's layout isn't documented well enough to trust, so heights come from the blocks:
        // the first air above the highest block, in the Data2D layout (int16, x + z * 16)
        private static byte[] LegacyTerrainHeights(byte[] terrain)
        {
            byte[] output = new byte[512];
            for (int bx = 0; bx < 16; bx++)
            {
                for (int bz = 0; bz < 16; bz++)
                {
                    int column = (bx * 16 + bz) * 128;
                    int height = 128;
                    while (height > 0 && terrain[column + height - 1] == 0) height--;
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan((bz * 16 + bx) * 2), (short)height);
                }
            }
            return output;
        }

        // Just the blocks, translated to Java names; used for the chunk itself and for its neighbours' light
        public BlockStateChunk? ReadBlocks(int x, int z, int dimension, IDictionary<byte[], byte[]> records, out byte[]? data3D)
        {
            BlockStateChunk blockState = new();
            data3D = null;

            foreach (var record in records)
            {
                if (!BedrockKey.TryParse(record.Key, out BedrockKey key) || key.Dimension != dimension) continue;
                if (key.X != x || key.Z != z) continue;

                if (key.Tag == BedrockChunkTag.Data3D)
                {
                    data3D = record.Value;
                }
                else if (key.Tag == BedrockChunkTag.SubChunkPrefix)
                {
                    PaletteSection<Block>? section = ReadSection(record.Value, key.SubChunkY);
                    if (section is not null) blockState.Sections.Add(section);
                }
                else if (key.Tag == BedrockChunkTag.LegacyTerrain && record.Value.Length >= LEGACY_TERRAIN_LENGTH)
                {
                    foreach (PaletteSection<Block> section in ReadLegacyTerrain(record.Value))
                    {
                        PaletteSection<Block>? translated = Translate(section);
                        if (translated is not null) blockState.Sections.Add(translated);
                    }
                }
            }

            return blockState.Sections.Count > 0 ? blockState : null;
        }

        // Layer 0 holds the blocks; layer 1 is the water a block is logged in, which Java draws as the block itself
        protected virtual PaletteSection<Block>? ReadSection(byte[] data, sbyte keyY)
        {
            BedrockSubChunkReader.SubChunk? subChunk = SubChunkReader.Read(data, keyY);
            if (subChunk is null || subChunk.Layers.Count < 1) return null;

            return Translate(subChunk.Layers[0]);
        }

        private PaletteSection<Block>? Translate(PaletteSection<Block> section)
        {
            BlockTranslator.Translate(section.Palette);

            if (RemoveEmptyAirSections && section.Palette.Length == 1 && section.Palette[0].Name == "minecraft:air") return null;
            return section;
        }
    }
}
