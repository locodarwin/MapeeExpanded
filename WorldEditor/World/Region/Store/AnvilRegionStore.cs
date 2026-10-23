namespace WorldEditor
{
    public class AnvilRegionStore : IRefreshableRegionStore, IChunkStampRegionStore
    {
        public int Count => _store.Count;

        // Replaced whole on a refresh, so readers on other threads always see one listing or the other
        private Dictionary<Coords, string> _store = [];
        private Dictionary<string, (long Length, DateTime Written)> _files = [];
        private string? _directory;
        private DateTime _lastLook;

        public IEnumerable<Coords> Itemize(string directory)
        {
            _directory = directory;
            _lastLook = DateTime.UtcNow;
            (_store, _files) = List(directory);
            return _store.Keys;
        }

        private static (Dictionary<Coords, string> Regions, Dictionary<string, (long, DateTime)> Files) List(string directory)
        {
            Dictionary<Coords, string> regions = [];
            Dictionary<string, (long, DateTime)> files = [];
            if (!Directory.Exists(directory)) return (regions, files);

            foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles())
            {
                if (!Parser.TryParseRegionName(file.Name, out int regionX, out int regionZ)) continue;
                regions[new Coords(regionX, regionZ)] = file.FullName;
                files[file.FullName] = (file.Length, file.LastWriteTimeUtc);
            }
            return (regions, files);
        }

        // Every region file's size and time, folded into one number
        public string Stamp()
        {
            if (_directory is null || !Directory.Exists(_directory)) return string.Empty;

            ulong hash = 14695981039346656037;
            foreach (FileInfo file in new DirectoryInfo(_directory).EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal))
            {
                if (!Parser.TryParseRegionName(file.Name, out _, out _)) continue;
                foreach (long part in new[] { file.Name.GetHashCode(), file.Length, file.LastWriteTimeUtc.Ticks })
                {
                    hash = (hash ^ (ulong)part) * 1099511628211;
                }
            }
            return hash.ToString();
        }

        // Region files that are new or have changed since the last look. Each one's header has the time every chunk
        // in it was last saved, which counts the chunks the game wrote
        public RegionChanges? Refresh()
        {
            if (_directory is null) return null;

            DateTime look = DateTime.UtcNow;
            var (regions, files) = List(_directory);
            long since = new DateTimeOffset(_lastLook).ToUnixTimeSeconds() - 2;

            List<Coords> changed = [];
            int chunks = 0;
            foreach (var (coords, file) in regions)
            {
                if (_files.TryGetValue(file, out var before) && before == files[file]) continue;
                changed.Add(coords);
                chunks += ChunksSavedSince(file, since);
            }

            _store = regions;
            _files = files;
            _lastLook = look;
            return changed.Count == 0 ? null : new RegionChanges(changed, chunks);
        }

        // The second 4 KiB of a region file: a big-endian save time in seconds for each of its 1,024 chunks
        public static int ChunksSavedSince(string file, long unixSeconds)
        {
            byte[] header = new byte[8192];
            using (FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) return 0;
            }

            int count = 0;
            for (int i = 0; i < 1024; i++)
            {
                long saved = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4096 + i * 4));
                if (saved != 0 && saved >= unixSeconds) count++;
            }
            return count;
        }

        // The first 8 KiB of each region file: where each chunk is (4 bytes) and when it was saved (4 bytes). A save
        // changes the time, and almost always the place too, so the two together stand for the chunk's contents
        public IReadOnlyDictionary<Coords, ulong> ChunkStamps()
        {
            Dictionary<Coords, ulong> stamps = [];
            byte[] header = new byte[8192];

            foreach (var (region, file) in _store)
            {
                try
                {
                    using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) continue;
                }
                catch (FileNotFoundException) { continue; }

                for (int i = 0; i < 1024; i++)
                {
                    uint location = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(i * 4));
                    if (location == 0) continue;

                    uint saved = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4096 + i * 4));
                    stamps[new Coords(region.X * 32 + i % 32, region.Z * 32 + i / 32)] = (ulong)location << 32 | saved;
                }
            }
            return stamps;
        }

        public bool Exists(Coords coords)
        {
            return _store.ContainsKey(coords);
        }

        public bool GetData(Coords coords, out byte[]? buffer, out StorageFormat format)
        {
            if (_store.TryGetValue(coords, out string? file))
            {
                // The game may be writing it; a short read leaves zeros, and the chunks there are skipped as unreadable
                using (FileStream fileStream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    buffer = new byte[fileStream.Length];
                    fileStream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                }

                format = Parser.ParseStorageFormat(Path.GetFileName(file));
                return true;
            }
            buffer = null;
            format = StorageFormat.Anvil;
            return false;
        }

        public byte[]? GetBuffer(Coords coords)
        {
            if (_store.TryGetValue(coords, out string? file))
            {
                return File.ReadAllBytes(file);
            }

            return null;
        }
    }
}
