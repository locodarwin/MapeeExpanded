using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace WorldEditor
{
    // A read-only sorted string table (.ldb/.sst). Safe to read from several threads at once
    public sealed class LevelDbTable : IDisposable
    {
        public ulong Number { get; }

        private const int FOOTER_SIZE = 48;
        private const ulong MAGIC = 0xdb4775248b80fb57;
        private const int MAX_CACHED_BLOCKS = 256;

        private readonly SafeFileHandle _handle;
        private readonly (byte[] LastKey, long Offset, int Size)[] _index;
        private readonly ConcurrentDictionary<long, byte[]> _cache = new();

        private delegate bool EntryVisitor(ReadOnlySpan<byte> internalKey, ReadOnlySpan<byte> value);

        public LevelDbTable(string file, ulong number)
        {
            Number = number;
            _handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);

            long length = RandomAccess.GetLength(_handle);
            byte[] footer = new byte[FOOTER_SIZE];
            RandomAccess.Read(_handle, footer, length - FOOTER_SIZE);

            if (BinaryPrimitives.ReadUInt64LittleEndian(footer.AsSpan(40)) != MAGIC)
            {
                throw new InvalidDataException($"{file} is not a LevelDB table");
            }

            int position = 0;
            LevelDbUtilities.ReadVarint64(footer, ref position); // metaindex offset
            LevelDbUtilities.ReadVarint64(footer, ref position); // metaindex size
            long indexOffset = (long)LevelDbUtilities.ReadVarint64(footer, ref position);
            int indexSize = (int)LevelDbUtilities.ReadVarint64(footer, ref position);

            List<(byte[], long, int)> index = [];
            VisitEntries(ReadBlock(indexOffset, indexSize), (key, value) =>
            {
                int valuePosition = 0;
                long offset = (long)LevelDbUtilities.ReadVarint64(value, ref valuePosition);
                int size = (int)LevelDbUtilities.ReadVarint64(value, ref valuePosition);
                index.Add((key.ToArray(), offset, size));
                return true;
            });
            _index = [.. index];
        }

        // Calls body for every entry whose user key starts with prefix, in key order
        public void ScanPrefix(ReadOnlySpan<byte> prefix, LevelDbEntryHandler body)
        {
            int lo = 0, hi = _index.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (LevelDbUtilities.Compare(LevelDbUtilities.UserKey(_index[mid].LastKey), prefix) < 0) lo = mid + 1;
                else hi = mid;
            }

            byte[] prefixCopy = prefix.ToArray();
            bool done = false;
            for (int i = lo; i < _index.Length && !done; i++)
            {
                VisitEntries(GetBlock(i), (key, value) =>
                {
                    ReadOnlySpan<byte> userKey = LevelDbUtilities.UserKey(key);
                    int comparison = LevelDbUtilities.ComparePrefix(userKey, prefixCopy);
                    if (comparison < 0) return true;
                    if (comparison > 0)
                    {
                        done = true;
                        return false;
                    }

                    LevelDbUtilities.ParseTrailer(key, out ulong sequence, out bool isDeletion);
                    body(userKey, sequence, isDeletion, value.ToArray());
                    return true;
                });
            }
        }

        // Visits every key in the table without copying values. Blocks read this way skip the cache,
        // since a full scan would only push out the blocks chunk reads are using
        public void ScanKeys(LevelDbKeyHandler body)
        {
            for (int i = 0; i < _index.Length; i++)
            {
                VisitEntries(ReadBlock(_index[i].Offset, _index[i].Size), (key, value) =>
                {
                    LevelDbUtilities.ParseTrailer(key, out ulong sequence, out bool isDeletion);
                    body(LevelDbUtilities.UserKey(key), sequence, isDeletion);
                    return true;
                });
            }
        }

        private byte[] GetBlock(int indexEntry)
        {
            var (_, offset, size) = _index[indexEntry];
            if (_cache.TryGetValue(offset, out byte[]? block)) return block;

            block = ReadBlock(offset, size);
            if (_cache.Count >= MAX_CACHED_BLOCKS) _cache.Clear();
            _cache[offset] = block;
            return block;
        }
        private byte[] ReadBlock(long offset, int size)
        {
            // Each block is followed by a 1 byte compression type and a 4 byte checksum
            byte[] raw = new byte[size + 1];
            RandomAccess.Read(_handle, raw, offset);
            return LevelDbUtilities.Decompress(raw, 0, size, raw[size]);
        }

        // Entries are prefix-compressed against the previous key: shared length, unshared length,
        // value length, the unshared key bytes, then the value
        private static void VisitEntries(byte[] block, EntryVisitor visitor)
        {
            ReadOnlySpan<byte> data = block;
            int restartCount = BinaryPrimitives.ReadInt32LittleEndian(data[^4..]);
            int end = data.Length - 4 - restartCount * 4;

            byte[] key = new byte[64];
            int position = 0;
            while (position < end)
            {
                int shared = LevelDbUtilities.ReadVarint32(data, ref position);
                int nonShared = LevelDbUtilities.ReadVarint32(data, ref position);
                int valueLength = LevelDbUtilities.ReadVarint32(data, ref position);

                if (shared + nonShared > key.Length) Array.Resize(ref key, Math.Max(key.Length * 2, shared + nonShared));
                data.Slice(position, nonShared).CopyTo(key.AsSpan(shared));
                position += nonShared;

                if (!visitor(key.AsSpan(0, shared + nonShared), data.Slice(position, valueLength))) return;
                position += valueLength;
            }
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    public delegate void LevelDbEntryHandler(ReadOnlySpan<byte> userKey, ulong sequence, bool isDeletion, byte[] value);
    public delegate void LevelDbKeyHandler(ReadOnlySpan<byte> userKey, ulong sequence, bool isDeletion);
}
