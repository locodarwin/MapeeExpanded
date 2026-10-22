using System.Buffers.Binary;
using System.IO.Compression;

namespace Mapee.Tests.Fixtures
{
    // A Java world folder written from scratch: a gzipped level.dat and Anvil region files. Deleted when disposed
    public sealed class JavaTestWorld : IDisposable
    {
        public const int DataVersion = 4903; // 26.2

        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "mapee-tests", Guid.NewGuid().ToString("N"));
        public string LevelDat => Path.Combine(Directory, "level.dat");

        // Dimension folder and region coordinates -> chunks in that region, each with the compression type its sector
        // header names
        private readonly Dictionary<(string Dimension, int X, int Z), List<(int X, int Z, byte Compression, byte[] Nbt)>> _regions = [];
        private Dictionary<string, object>? _player;

        // compression: 1 gzip, 2 zlib (what the game writes), 3 none. dimension: the game's folder under
        // dimensions/minecraft (overworld, the_nether, the_end)
        public JavaTestWorld Chunk(int x, int z, Dictionary<string, object> chunk, byte compression = 2, string dimension = "overworld")
        {
            var key = (dimension, x >> 5, z >> 5);
            if (!_regions.TryGetValue(key, out var chunks)) _regions[key] = chunks = [];
            chunks.Add((x, z, compression, Nbt.Compound(chunk, littleEndian: false)));
            return this;
        }

        // The single player, written as 26.x does: players/data/<uuid>.dat, named in level.dat as singleplayer_uuid
        public JavaTestWorld Player(Dictionary<string, object> player)
        {
            _player = player;
            return this;
        }

        public static readonly int[] PlayerUuid = [0x12345678, unchecked((int)0x9abcdef0), 0x11223344, 0x55667788];
        public const string PlayerFile = "12345678-9abc-def0-1122-334455667788.dat";

        public JavaTestWorld Save(string worldName = "Java test world")
        {
            Dictionary<string, object> data = new()
            {
                ["DataVersion"] = DataVersion,
                ["Version"] = new Dictionary<string, object> { ["Name"] = "26.2", ["Id"] = DataVersion },
                ["LevelName"] = worldName,
                ["GameType"] = 1,
                ["SpawnX"] = 0,
                ["SpawnY"] = 64,
                ["SpawnZ"] = 0,
                ["LastPlayed"] = 1700000000000L
            };
            if (_player is not null) data["singleplayer_uuid"] = PlayerUuid;
            System.IO.Directory.CreateDirectory(Directory);
            WriteGzipped(LevelDat, Nbt.Compound(new() { ["Data"] = data }, littleEndian: false));

            if (_player is not null)
            {
                string players = Path.Combine(Directory, "players", "data");
                System.IO.Directory.CreateDirectory(players);
                WriteGzipped(Path.Combine(players, PlayerFile), Nbt.Compound(_player, littleEndian: false));
            }

            // The game's own layout since 26.1, spelled out rather than asked of Mapee, so a wrong folder there shows
            foreach (var (region, chunks) in _regions)
            {
                string regionFolder = Path.Combine(Directory, "dimensions", "minecraft", region.Dimension, "region");
                System.IO.Directory.CreateDirectory(regionFolder);
                File.WriteAllBytes(Path.Combine(regionFolder, $"r.{region.X}.{region.Z}.mca"), RegionFile(chunks));
            }
            return this;
        }

        private static void WriteGzipped(string path, byte[] bytes)
        {
            using FileStream file = File.Create(path);
            using GZipStream gzip = new(file, CompressionLevel.Optimal);
            gzip.Write(bytes);
        }

        // An 8 KiB header of sector locations and timestamps, then each chunk in whole 4 KiB sectors:
        // a big-endian length, the compression type, and the compressed NBT
        private static byte[] RegionFile(List<(int X, int Z, byte Compression, byte[] Nbt)> chunks)
        {
            const int sector = 4096;
            using MemoryStream stream = new();
            stream.Write(new byte[sector * 2]);

            byte[] header = new byte[sector];
            foreach (var (x, z, compression, nbt) in chunks)
            {
                byte[] payload = Compress(nbt, compression);
                int offset = (int)(stream.Length / sector);
                int length = payload.Length + 1;
                int sectors = (length + 4 + sector - 1) / sector;

                byte[] lengthBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lengthBytes, length);
                stream.Write(lengthBytes);
                stream.WriteByte(compression);
                stream.Write(payload);
                stream.Write(new byte[sectors * sector - (length + 4)]);

                int index = ((x & 31) + (z & 31) * 32) * 4;
                header[index] = (byte)(offset >> 16);
                header[index + 1] = (byte)(offset >> 8);
                header[index + 2] = (byte)offset;
                header[index + 3] = (byte)sectors;
            }

            byte[] output = stream.ToArray();
            header.CopyTo(output, 0);
            return output;
        }

        private static byte[] Compress(byte[] data, byte compression)
        {
            if (compression == 3) return data;

            using MemoryStream output = new();
            using (Stream compressor = compression == 1 ? new GZipStream(output, CompressionLevel.Optimal) : new ZLibStream(output, CompressionLevel.Optimal))
            {
                compressor.Write(data);
            }
            return output.ToArray();
        }

        // Java packs palette indexes into longs without spanning them, at least 4 bits each, in YZX order
        public static long[] PackBlockStates(int paletteLength, Func<int, int, int, int> indexAt)
        {
            int bits = Math.Max(4, (int)Math.Ceiling(Math.Log2(paletteLength)));
            int perLong = 64 / bits;
            long[] output = new long[(4096 + perLong - 1) / perLong];
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                    {
                        int i = y << 8 | z << 4 | x;
                        output[i / perLong] |= (long)indexAt(x, y, z) << (i % perLong * bits);
                    }
            return output;
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
