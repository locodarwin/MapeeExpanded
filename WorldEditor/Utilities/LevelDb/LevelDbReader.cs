using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace WorldEditor
{
    // A read-only view of a LevelDB database as it stood when opened. Bedrock uses Mojang's fork,
    // which adds zlib and raw deflate block compression and is otherwise the standard format
    public sealed class LevelDbReader : IDisposable
    {
        public string Directory { get; }

        // Tables open on first use, so a lookup touching a few of them doesn't open them all
        private readonly Lazy<LevelDbTable>[] _tables;
        private readonly LevelDbManifest.FileEntry[] _tableEntries;
        private readonly SortedList<byte[], (ulong Sequence, byte[]? Value)> _memory = new(ByteArrayComparer.Instance);

        public LevelDbReader(string directory)
        {
            Directory = directory;

            LevelDbManifest manifest = LevelDbManifest.Read(directory);
            _tableEntries = [.. manifest.Files];
            _tables = new Lazy<LevelDbTable>[_tableEntries.Length];

            for (int i = 0; i < _tableEntries.Length; i++)
            {
                ulong number = _tableEntries[i].Number;
                _tables[i] = new Lazy<LevelDbTable>(() => new LevelDbTable(FindTableFile(directory, number), number));
            }

            ulong firstLog = manifest.PrevLogNumber != 0 ? Math.Min(manifest.PrevLogNumber, manifest.LogNumber) : manifest.LogNumber;
            foreach (string log in System.IO.Directory.GetFiles(directory, "*.log").Order())
            {
                if (ulong.TryParse(Path.GetFileNameWithoutExtension(log), out ulong number) && number >= firstLog)
                {
                    ReadLog(log);
                }
            }
        }

        // Returns every live key starting with prefix, with its newest value
        public IDictionary<byte[], byte[]> GetByPrefix(ReadOnlySpan<byte> prefix)
        {
            Dictionary<byte[], (ulong Sequence, byte[]? Value)> found = new(ByteArrayComparer.Instance);

            for (int i = 0; i < _tables.Length; i++)
            {
                if (LevelDbUtilities.ComparePrefix(_tableEntries[i].SmallestUserKey, prefix) > 0) continue;
                if (LevelDbUtilities.ComparePrefix(_tableEntries[i].LargestUserKey, prefix) < 0) continue;

                _tables[i].Value.ScanPrefix(prefix, (key, sequence, isDeletion, value) => Keep(found, key.ToArray(), sequence, isDeletion ? null : value));
            }

            foreach (var entry in MemoryWithPrefix(prefix))
            {
                Keep(found, entry.Key, entry.Value.Sequence, entry.Value.Value);
            }

            Dictionary<byte[], byte[]> output = new(ByteArrayComparer.Instance);
            foreach (var entry in found)
            {
                if (entry.Value.Value is not null) output[entry.Key] = entry.Value.Value;
            }
            return output;
        }
        public bool TryGet(byte[] key, out byte[]? value)
        {
            IDictionary<byte[], byte[]> found = GetByPrefix(key);
            return found.TryGetValue(key, out value);
        }

        // Visits every live key accepted by filter once, without reading values. Tables are scanned
        // in parallel; only keys passing the filter are copied, so a filter that picks out few keys keeps this cheap
        public void ForEachKey(LevelDbKeyFilter filter, Action<byte[]> body)
        {
            byte[] present = [];
            ConcurrentBag<Dictionary<byte[], (ulong Sequence, byte[]? Value)>> partials = [];

            Parallel.For(0, _tables.Length, () => new Dictionary<byte[], (ulong, byte[]?)>(ByteArrayComparer.Instance), (i, _, found) =>
            {
                _tables[i].Value.ScanKeys((key, sequence, isDeletion) =>
                {
                    if (filter(key)) Keep(found, key.ToArray(), sequence, isDeletion ? null : present);
                });
                return found;
            }, partials.Add);

            Dictionary<byte[], (ulong Sequence, byte[]? Value)> merged = new(ByteArrayComparer.Instance);
            foreach (var partial in partials)
            {
                foreach (var entry in partial) Keep(merged, entry.Key, entry.Value.Sequence, entry.Value.Value);
            }
            foreach (var entry in _memory)
            {
                if (filter(entry.Key)) Keep(merged, entry.Key, entry.Value.Sequence, entry.Value.Value is null ? null : present);
            }

            foreach (var entry in merged)
            {
                if (entry.Value.Value is not null) body(entry.Key);
            }
        }

        private static void Keep(Dictionary<byte[], (ulong Sequence, byte[]? Value)> found, byte[] key, ulong sequence, byte[]? value)
        {
            if (found.TryGetValue(key, out var existing) && existing.Sequence >= sequence) return;
            found[key] = (sequence, value);
        }

        private IEnumerable<KeyValuePair<byte[], (ulong Sequence, byte[]? Value)>> MemoryWithPrefix(ReadOnlySpan<byte> prefix)
        {
            IList<byte[]> keys = _memory.Keys;
            int lo = 0, hi = keys.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (LevelDbUtilities.Compare(keys[mid], prefix) < 0) lo = mid + 1;
                else hi = mid;
            }

            List<KeyValuePair<byte[], (ulong, byte[]?)>> output = [];
            for (int i = lo; i < keys.Count && LevelDbUtilities.ComparePrefix(keys[i], prefix) == 0; i++)
            {
                output.Add(new(keys[i], _memory.Values[i]));
            }
            return output;
        }

        // A log record is a write batch: an 8 byte sequence, a 4 byte count, then put/delete operations
        private void ReadLog(string file)
        {
            foreach (byte[] record in LevelDbLogReader.ReadRecords(file))
            {
                if (record.Length < 12) continue;

                ReadOnlySpan<byte> data = record;
                ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(data);
                int count = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
                int position = 12;

                for (int i = 0; i < count && position < data.Length; i++, sequence++)
                {
                    byte type = data[position++];
                    byte[] key = LevelDbUtilities.ReadLengthPrefixed(data, ref position);
                    byte[]? value = type == 1 ? LevelDbUtilities.ReadLengthPrefixed(data, ref position) : null;

                    if (_memory.TryGetValue(key, out var existing) && existing.Sequence >= sequence) continue;
                    _memory[key] = (sequence, value);
                }
            }
        }

        private static string FindTableFile(string directory, ulong number)
        {
            string ldb = Path.Combine(directory, $"{number:D6}.ldb");
            if (File.Exists(ldb)) return ldb;

            string sst = Path.Combine(directory, $"{number:D6}.sst");
            if (File.Exists(sst)) return sst;

            throw new FileNotFoundException($"Table {number} listed in the manifest is missing", ldb);
        }

        public void Dispose()
        {
            foreach (Lazy<LevelDbTable> table in _tables)
            {
                if (table.IsValueCreated) table.Value.Dispose();
            }
        }
    }

    public delegate bool LevelDbKeyFilter(ReadOnlySpan<byte> userKey);

    public sealed class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj)
        {
            HashCode hash = new();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
