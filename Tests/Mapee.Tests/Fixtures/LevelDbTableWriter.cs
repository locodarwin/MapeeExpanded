using System.Buffers.Binary;
using System.IO.Compression;
using WorldEditor;

namespace Mapee.Tests.Fixtures
{
    // Writes a LevelDB sorted table: data blocks of entries, an index block naming each block's last key,
    // an empty metaindex block and a footer. Keys are internal keys (user key + sequence and type), sorted.
    // Entries aren't prefix-compressed, which the format allows; blocks are kept small so a table has several
    public static class LevelDbTableWriter
    {
        private const ulong MAGIC = 0xdb4775248b80fb57;
        private const int ENTRIES_PER_BLOCK = 3;

        public static byte[] Write(IList<(byte[] Key, byte[] Value)> records, ulong firstSequence, byte compression, out byte[] smallest, out byte[] largest)
        {
            var entries = records.Select((r, i) => (Key: InternalKey(r.Key, firstSequence + (ulong)i, 1), r.Value))
                .OrderBy(e => e.Key, ByteArrayComparer.Instance).ToList();
            smallest = entries[0].Key;
            largest = entries[^1].Key;

            using MemoryStream file = new();
            List<(byte[] LastKey, long Offset, int Size)> index = [];
            for (int i = 0; i < entries.Count; i += ENTRIES_PER_BLOCK)
            {
                var block = entries.Skip(i).Take(ENTRIES_PER_BLOCK).ToList();
                var (offset, size) = WriteBlock(file, Block(block), compression);
                index.Add((block[^1].Key, offset, size));
            }

            var metaindex = WriteBlock(file, Block([]), 0);
            var indexHandle = WriteBlock(file, Block(index.Select(e => (e.LastKey, Handle(e.Offset, e.Size))).ToList()), 0);

            using MemoryStream footer = new();
            footer.Write(Handle(metaindex.Offset, metaindex.Size));
            footer.Write(Handle(indexHandle.Offset, indexHandle.Size));
            footer.Write(new byte[40 - footer.Length]);
            footer.Write(BitConverter.GetBytes(MAGIC));
            file.Write(footer.ToArray());
            return file.ToArray();
        }

        // A version edit naming the log to replay and adding the table at level 0
        public static byte[] ManifestEdit(ulong logNumber, ulong tableNumber, long tableSize, byte[] smallest, byte[] largest)
        {
            using MemoryStream stream = new();
            BedrockTestWorld.WriteVarint(stream, 2);
            BedrockTestWorld.WriteVarint(stream, logNumber);
            BedrockTestWorld.WriteVarint(stream, 7);
            BedrockTestWorld.WriteVarint(stream, 0);
            BedrockTestWorld.WriteVarint(stream, tableNumber);
            BedrockTestWorld.WriteVarint(stream, (ulong)tableSize);
            BedrockTestWorld.WriteVarint(stream, (ulong)smallest.Length);
            stream.Write(smallest);
            BedrockTestWorld.WriteVarint(stream, (ulong)largest.Length);
            stream.Write(largest);
            return stream.ToArray();
        }

        public static byte[] InternalKey(byte[] userKey, ulong sequence, byte type)
        {
            byte[] output = new byte[userKey.Length + 8];
            userKey.CopyTo(output, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(userKey.Length), sequence << 8 | type);
            return output;
        }

        // shared 0, key length, value length, key, value; then one restart point at the start
        private static byte[] Block(IList<(byte[] Key, byte[] Value)> entries)
        {
            using MemoryStream stream = new();
            foreach (var (key, value) in entries)
            {
                BedrockTestWorld.WriteVarint(stream, 0);
                BedrockTestWorld.WriteVarint(stream, (ulong)key.Length);
                BedrockTestWorld.WriteVarint(stream, (ulong)value.Length);
                stream.Write(key);
                stream.Write(value);
            }
            stream.Write(BitConverter.GetBytes(0));
            stream.Write(BitConverter.GetBytes(1));
            return stream.ToArray();
        }

        // The block, then its compression type and a checksum (not checked by Mapee's reader)
        private static (long Offset, int Size) WriteBlock(Stream file, byte[] block, byte compression)
        {
            byte[] stored = compression switch
            {
                0 => block,
                2 => Compress(block, s => new ZLibStream(s, CompressionLevel.Optimal)),
                4 => Compress(block, s => new DeflateStream(s, CompressionLevel.Optimal)),
                _ => throw new ArgumentException($"Unknown compression {compression}")
            };

            long offset = file.Position;
            file.Write(stored);
            file.WriteByte(compression);
            file.Write(new byte[4]);
            return (offset, stored.Length);
        }

        private static byte[] Compress(byte[] data, Func<Stream, Stream> compressor)
        {
            using MemoryStream output = new();
            using (Stream stream = compressor(output))
            {
                stream.Write(data);
            }
            return output.ToArray();
        }

        private static byte[] Handle(long offset, int size)
        {
            using MemoryStream stream = new();
            BedrockTestWorld.WriteVarint(stream, (ulong)offset);
            BedrockTestWorld.WriteVarint(stream, (ulong)size);
            return stream.ToArray();
        }
    }
}
