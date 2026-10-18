using System.Buffers.Binary;
using WorldEditor;

namespace Mapee.Tests.Fixtures
{
    // A Bedrock world folder written from scratch: level.dat and a LevelDB database. Records go in the
    // write-ahead log, or with a table compression into a sorted table the manifest lists, as a compacted
    // world has them; Update and Delete then land in the log on top, as newer writes do. Deleted when disposed
    public sealed class BedrockTestWorld : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));
        public string LevelDat => Path.Combine(Directory, "level.dat");

        private readonly List<(byte[] Key, byte[] Value)> _records = [];
        private readonly List<(byte[] Key, byte[]? Value)> _later = [];

        public BedrockTestWorld Put(byte[] key, byte[] value)
        {
            _records.Add((key, value));
            return this;
        }
        public BedrockTestWorld Update(byte[] key, byte[] value)
        {
            _later.Add((key, value));
            return this;
        }
        public BedrockTestWorld Delete(byte[] key)
        {
            _later.Add((key, null));
            return this;
        }

        public static byte[] Key(int x, int z, int dimension, BedrockChunkTag tag, sbyte? subChunkY = null)
        {
            byte[] prefix = BedrockKey.ChunkPrefix(x, z, dimension);
            return [.. prefix, (byte)tag, .. subChunkY is sbyte y ? new[] { (byte)y } : []];
        }

        // Every chunk needs a version record to be indexed; worlds before 1.16.100 used tag 118 for it
        public BedrockTestWorld Chunk(int x, int z, int dimension = 0, bool legacyVersionKey = false, byte version = 40)
        {
            return Put(Key(x, z, dimension, legacyVersionKey ? BedrockChunkTag.LegacyVersion : BedrockChunkTag.Version), [version]);
        }

        // tableCompression: null keeps everything in the log; 0 none, 2 zlib, 4 raw deflate (what Bedrock writes)
        public BedrockTestWorld Save(string worldName = "Test world", byte? tableCompression = null)
        {
            string db = Path.Combine(Directory, "db");
            System.IO.Directory.CreateDirectory(db);

            byte[] nbt = Nbt.Compound(new()
            {
                ["LevelName"] = worldName,
                ["StorageVersion"] = 10,
                ["GameType"] = 1,
                ["SpawnX"] = 0,
                ["SpawnY"] = 64,
                ["SpawnZ"] = 0,
                ["LastPlayed"] = 1700000000L
            });
            byte[] header = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(header, 10);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), nbt.Length);
            File.WriteAllBytes(LevelDat, [.. header, .. nbt]);

            File.WriteAllText(Path.Combine(db, "CURRENT"), "MANIFEST-000001\n");
            List<(byte[] Key, byte[]? Value)> log = [.. _later];
            if (tableCompression is byte compression)
            {
                byte[] table = LevelDbTableWriter.Write(_records, 1, compression, out byte[] smallest, out byte[] largest);
                File.WriteAllBytes(Path.Combine(db, "000005.ldb"), table);
                File.WriteAllBytes(Path.Combine(db, "MANIFEST-000001"), LogFile(LevelDbTableWriter.ManifestEdit(logNumber: 6, tableNumber: 5, table.Length, smallest, largest)));
                File.WriteAllBytes(Path.Combine(db, "000006.log"), LogFile(WriteBatch(log, (ulong)_records.Count + 1)));
            }
            else
            {
                File.WriteAllBytes(Path.Combine(db, "MANIFEST-000001"), []);
                File.WriteAllBytes(Path.Combine(db, "000002.log"), LogFile(WriteBatch([.. _records.Select(r => (r.Key, (byte[]?)r.Value)), .. log], 1)));
            }
            return this;
        }

        // sequence, count, then per record a put (type 1, key, value) or a deletion (type 0, key), lengths as varints
        private static byte[] WriteBatch(IList<(byte[] Key, byte[]? Value)> records, ulong sequence)
        {
            using MemoryStream stream = new();
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteUInt64LittleEndian(header, sequence);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], records.Count);
            stream.Write(header);
            foreach (var (key, value) in records)
            {
                stream.WriteByte(value is null ? (byte)0 : (byte)1);
                WriteVarint(stream, (uint)key.Length);
                stream.Write(key);
                if (value is null) continue;
                WriteVarint(stream, (uint)value.Length);
                stream.Write(value);
            }
            return stream.ToArray();
        }

        // Log framing: 32 KiB blocks of records with a 7 byte header (checksum, length, type); a record that
        // doesn't fit is split into first/middle/last fragments, and a block tail too short for a header is padding
        public static byte[] LogFile(params byte[][] records)
        {
            const int block = 32768, headerSize = 7;
            using MemoryStream stream = new();
            foreach (byte[] record in records)
            {
                int position = 0;
                bool first = true;
                do
                {
                    int left = block - (int)(stream.Length % block);
                    if (left < headerSize)
                    {
                        stream.Write(new byte[left]);
                        left = block;
                    }

                    int length = Math.Min(record.Length - position, left - headerSize);
                    bool last = position + length == record.Length;
                    byte type = (first, last) switch { (true, true) => 1, (true, false) => 2, (false, false) => 3, _ => 4 };

                    stream.Write([0, 0, 0, 0, (byte)length, (byte)(length >> 8), type]);
                    stream.Write(record, position, length);
                    position += length;
                    first = false;
                } while (position < record.Length);
            }
            return stream.ToArray();
        }

        public static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
