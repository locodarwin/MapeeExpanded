using WorldEditor;

namespace Mapper
{
    // Where to look and how the map is drawn there
    public readonly record struct InspectRequest(IRegionStore RegionStore, int X, int Z, int? StartY, bool Caves);

    // Looks up the block under the mouse off the UI thread. The mouse moves faster than chunks read, so only the
    // newest request waiting is served and older ones are dropped. Inspected is raised on a background thread
    public class BlockInspector
    {
        public event Action<InspectRequest, InspectedColumn?>? Inspected;

        private readonly object _lock = new();
        private InspectRequest? _pending;
        private bool _running;

        private ChunkLookup? _lookup;
        private Coords? _chunkCoords;
        private ConvertedApiChunk? _chunk;
        private DateTime _chunkReadAt;

        // A chunk is reused while the mouse stays in it, but reread now and then in case the game saved it
        private static readonly TimeSpan ChunkLifetime = TimeSpan.FromSeconds(2);

        public void Inspect(InspectRequest request)
        {
            lock (_lock)
            {
                _pending = request;
                if (_running) return;
                _running = true;
            }

            Task.Run(Run);
        }

        private void Run()
        {
            while (true)
            {
                InspectRequest request;
                lock (_lock)
                {
                    if (_pending is not InspectRequest next)
                    {
                        _running = false;
                        return;
                    }
                    request = next;
                    _pending = null;
                }

                InspectedColumn? column = null;
                try
                {
                    column = InspectNow(request);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Couldn't inspect {request.X}, {request.Z}: {e.Message}");
                }
                Inspected?.Invoke(request, column);
            }
        }

        // Synchronous, for tests and tools; not to be mixed with Inspect on the same instance
        public InspectedColumn? InspectNow(InspectRequest request)
        {
            if (_lookup?.RegionStore != request.RegionStore)
            {
                _lookup = new ChunkLookup(request.RegionStore);
                _chunkCoords = null;
            }

            Coords coords = new(MathUtilities.FindSectionY(request.X), MathUtilities.FindSectionY(request.Z));
            if (_chunkCoords is not Coords cached || !cached.Equals(coords) || DateTime.UtcNow - _chunkReadAt > ChunkLifetime)
            {
                _chunk?.Dispose();
                _chunk = _lookup.Read(coords);
                _chunkCoords = coords;
                _chunkReadAt = DateTime.UtcNow;
            }
            if (_chunk is null) return null;

            return ColumnInspector.Inspect(_chunk, MathUtilities.NegMod(request.X, 16), MathUtilities.NegMod(request.Z, 16), request.StartY, request.Caves);
        }
    }
}
