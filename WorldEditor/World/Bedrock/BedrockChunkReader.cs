namespace WorldEditor
{
    // Builds the same ApiChunk a Java chunk produces, from one chunk's Bedrock records.
    // Bedrock doesn't save light, so sky and block light are computed (see BedrockLight)
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
                // A copy of the list, since Mapee clears a chunk's sections once it has drawn them
                blockState = new BlockStateChunk(blocks.Sections.Count);
                foreach (PaletteSection<Block> section in blocks.Sections) blockState.Sections.Add(section);
                data3D = records.FirstOrDefault(r => BedrockKey.TryParse(r.Key, out BedrockKey key) && key.Tag == BedrockChunkTag.Data3D && key.Dimension == dimension && key.X == x && key.Z == z).Value;
            }
            if (blockState is null) return null;

            ApiChunk output = new()
            {
                X = x,
                Z = z,
                Version = Version.Newest,
                BlockState = blockState,
                SkyLight = ComputeSkyLight ? BedrockLight.SkyLight(blockState, dimension) : null
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
            }

            return blockState.Sections.Count > 0 ? blockState : null;
        }

        // Layer 0 holds the blocks; layer 1 is the water a block is logged in, which Java draws as the block itself
        protected virtual PaletteSection<Block>? ReadSection(byte[] data, sbyte keyY)
        {
            BedrockSubChunkReader.SubChunk? subChunk = SubChunkReader.Read(data, keyY);
            if (subChunk is null || subChunk.Layers.Count < 1) return null;

            PaletteSection<Block> section = subChunk.Layers[0];
            BlockTranslator.Translate(section.Palette);

            if (RemoveEmptyAirSections && section.Palette.Length == 1 && section.Palette[0].Name == "minecraft:air") return null;
            return section;
        }
    }
}
