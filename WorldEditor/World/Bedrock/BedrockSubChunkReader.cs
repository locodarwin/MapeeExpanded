using NbtEditor;
using System.Buffers.Binary;

namespace WorldEditor
{
    // Reads SubChunkPrefix records. Versions 1, 8 and 9 hold block palettes; 0 and 2 to 7 (before 1.2.13) hold
    // numeric IDs and data values, which come out as BedrockBlockTranslator.Legacy blocks
    public class BedrockSubChunkReader
    {
        public class SubChunk
        {
            public sbyte Y { get; init; }
            public IList<PaletteSection<Block>> Layers { get; init; } = [];
        }

        public SubChunk? Read(byte[] data, sbyte keyY)
        {
            if (data.Length < 1) return null;

            int position = 0;
            byte version = data[position++];

            int layerCount;
            sbyte y = keyY;
            switch (version)
            {
                case 1:
                    layerCount = 1;
                    break;
                case 8:
                    layerCount = data[position++];
                    break;
                case 9:
                    layerCount = data[position++];
                    y = (sbyte)data[position++];
                    break;
                case 0:
                case >= 2 and <= 7:
                    if (data.Length < 1 + 4096 + 2048) return null;
                    return new SubChunk() { Y = y, Layers = [ReadNumeric(data.AsSpan(1, 4096), data.AsSpan(1 + 4096, 2048), y)] };
                default:
                    return null;
            }

            SubChunk output = new() { Y = y };
            for (int i = 0; i < layerCount; i++)
            {
                output.Layers.Add(ReadLayer(data, ref position, y));
            }
            return output;
        }

        protected virtual PaletteSection<Block> ReadLayer(byte[] data, ref int position, sbyte y)
        {
            int bitCount = data[position++] >> 1;

            long[] words;
            int paletteLength;
            if (bitCount == 0)
            {
                // A uniform layer has no words and no palette length; its single entry follows directly
                words = [];
                paletteLength = 1;
            }
            else
            {
                int perWord = 32 / bitCount;
                int wordCount = (4096 + perWord - 1) / perWord;

                words = new long[wordCount];
                for (int i = 0; i < wordCount; i++)
                {
                    words[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
                    position += 4;
                }

                paletteLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
                position += 4;
            }

            FastBufferProvider buffer = new(data, position);
            UnsafeNbtReader reader = new(buffer, ArrayPoolAllocation.CreateDefault()) { IsFormatLittleEndian = true };
            TagDeserializer deserializer = new();

            Block[] palette = new Block[paletteLength];
            for (int i = 0; i < paletteLength; i++)
            {
                palette[i] = ReadBlock(deserializer.Deserialize(reader));
            }
            position = buffer.Index;

            return new PaletteSection<Block>(palette, words)
            {
                Y = y,
                Locker = new BedrockBlockStateLocker(bitCount, paletteLength)
            };
        }

        // ids and data are in XZY order like palette indexes, data two values per byte, low nibble first
        public static PaletteSection<Block> ReadNumeric(ReadOnlySpan<byte> ids, ReadOnlySpan<byte> data, sbyte y)
        {
            Dictionary<int, int> paletteIndexes = [];
            List<Block> palette = [];
            int[] values = new int[4096];

            for (int i = 0; i < 4096; i++)
            {
                int value = (data[i >> 1] >> ((i & 1) * 4)) & 15;
                int key = ids[i] << 4 | value;
                if (!paletteIndexes.TryGetValue(key, out int index))
                {
                    paletteIndexes[key] = index = palette.Count;
                    palette.Add(BedrockBlockTranslator.Legacy(ids[i].ToString(), value));
                }
                values[i] = index;
            }

            return Pack(values, [.. palette], y);
        }

        // Packs palette indexes the way version 8 stores them, so numeric sections share BedrockBlockStateLocker
        public static PaletteSection<Block> Pack(int[] values, Block[] palette, sbyte y)
        {
            if (palette.Length == 1)
            {
                return new PaletteSection<Block>(palette, []) { Y = y, Locker = new BedrockBlockStateLocker(0, 1) };
            }

            int bitCount = new[] { 1, 2, 3, 4, 5, 6, 8, 16 }.First(bits => (1 << bits) >= palette.Length);
            int perWord = 32 / bitCount;
            long[] words = new long[(4096 + perWord - 1) / perWord];
            for (int i = 0; i < values.Length; i++)
            {
                words[i / perWord] |= (long)((uint)values[i] << (i % perWord * bitCount));
            }

            return new PaletteSection<Block>(palette, words) { Y = y, Locker = new BedrockBlockStateLocker(bitCount, palette.Length) };
        }

        protected virtual Block ReadBlock(Tag tag)
        {
            if (tag is not CompoundTag compound || compound["name"] is not StringTag nameTag) return new Block("minecraft:air");

            // Palettes from 1.2.13 to 1.12 store a data value in place of states
            if (compound["states"] is null && compound["val"] is Tag valueTag && valueTag.Id == TagId.Int16)
            {
                return BedrockBlockTranslator.Legacy(nameTag, (short)valueTag);
            }

            if (compound["states"] is not CompoundTag states || states.Count < 1) return new Block(nameTag);

            Property[] properties = new Property[states.Count];
            int i = 0;
            foreach (var state in states)
            {
                properties[i++] = new Property(state.Key, StateValue(state.Value));
            }
            return new Block(nameTag, properties);
        }
        private static string StateValue(Tag value)
        {
            return value.Id switch
            {
                TagId.String => (string)value,
                TagId.SignedByte => ((sbyte)value).ToString(),
                TagId.Int16 => ((short)value).ToString(),
                TagId.Int32 => ((int)value).ToString(),
                TagId.Int64 => ((long)value).ToString(),
                _ => string.Empty
            };
        }
    }
}
