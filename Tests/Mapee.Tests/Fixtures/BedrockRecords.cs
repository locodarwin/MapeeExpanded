using System.Buffers.Binary;
using WorldEditor;

namespace Mapee.Tests.Fixtures
{
    // Builds chunk records in each format Bedrock has saved. Positions use x, y, z within the chunk
    public static class BedrockRecords
    {
        public static int XZY(int x, int y, int z) => x << 8 | z << 4 | y;

        // Versions 8 and 9: palette indexes packed into 32-bit words, then a little-endian NBT palette
        public static byte[] PaletteSubChunk(byte version, sbyte y, Dictionary<string, object>[] palette, Func<int, int, int, int> indexAt)
        {
            int[] values = new int[4096];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    for (int by = 0; by < 16; by++)
                        values[XZY(x, by, z)] = indexAt(x, by, z);

            using MemoryStream stream = new();
            stream.WriteByte(version);
            stream.WriteByte(1);
            if (version >= 9) stream.WriteByte((byte)y);
            WriteStorage(stream, values, palette.Length);
            foreach (var entry in palette) stream.Write(Nbt.Compound(entry));
            return stream.ToArray();
        }

        // Versions 0 and 2 to 7: 4096 IDs and 2048 bytes of data values in XZY order
        public static byte[] NumericSubChunk(byte version, Func<int, int, int, (byte Id, byte Data)> blockAt)
        {
            byte[] output = new byte[1 + 4096 + 2048 + 4096];
            output[0] = version;
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    for (int y = 0; y < 16; y++)
                    {
                        int i = XZY(x, y, z);
                        (byte id, byte data) = blockAt(x, y, z);
                        output[1 + i] = id;
                        output[1 + 4096 + (i >> 1)] |= (byte)((data & 15) << ((i & 1) * 4));
                    }
            return output;
        }

        // Before 1.0: the whole 128 high chunk in one record
        public static byte[] LegacyTerrain(Func<int, int, int, (byte Id, byte Data)> blockAt, Func<int, int, byte> biomeAt)
        {
            const int blocks = 16 * 16 * 128;
            byte[] output = new byte[blocks + blocks / 2 * 3 + 256 + 1024];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                {
                    for (int y = 0; y < 128; y++)
                    {
                        int i = x * 2048 + z * 128 + y;
                        (byte id, byte data) = blockAt(x, y, z);
                        output[i] = id;
                        output[blocks + (i >> 1)] |= (byte)((data & 15) << ((i & 1) * 4));
                    }
                    output[blocks + blocks / 2 * 3 + 256 + (z * 16 + x) * 4] = biomeAt(x, z);
                }
            return output;
        }

        // Data2D: 256 int16 heights then 256 biome IDs, both x + z * 16
        public static byte[] Data2D(Func<int, int, short> heightAt, Func<int, int, byte> biomeAt)
        {
            byte[] output = new byte[512 + 256];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan((z * 16 + x) * 2), heightAt(x, z));
                    output[512 + z * 16 + x] = biomeAt(x, z);
                }
            return output;
        }

        // Data3D: heights from the dimension floor, then one biome storage per section; uniform ones are cheapest
        public static byte[] Data3D(Func<int, int, short> heightAboveFloor, int biome, int sections = 24)
        {
            using MemoryStream stream = new();
            byte[] heights = new byte[512];
            for (int x = 0; x < 16; x++)
                for (int z = 0; z < 16; z++)
                    BinaryPrimitives.WriteInt16LittleEndian(heights.AsSpan((z * 16 + x) * 2), heightAboveFloor(x, z));
            stream.Write(heights);
            for (int i = 0; i < sections; i++)
            {
                stream.WriteByte(0);
                stream.Write(BitConverter.GetBytes(biome));
            }
            return stream.ToArray();
        }

        private static void WriteStorage(Stream stream, int[] values, int paletteLength)
        {
            int bitCount = new[] { 1, 2, 3, 4, 5, 6, 8, 16 }.First(b => (1 << b) >= Math.Max(2, paletteLength));
            int perWord = 32 / bitCount;
            uint[] words = new uint[(4096 + perWord - 1) / perWord];
            for (int i = 0; i < 4096; i++) words[i / perWord] |= (uint)values[i] << (i % perWord * bitCount);

            stream.WriteByte((byte)(bitCount << 1));
            foreach (uint word in words) stream.Write(BitConverter.GetBytes(word));
            stream.Write(BitConverter.GetBytes(paletteLength));
        }
    }
}
