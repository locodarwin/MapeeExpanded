using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldEditor;
#if AVALONIA
using Avalonia.Threading;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    // Where mobs can spawn, region by region, for the spawn overlay of either GUI. The painter says which regions are
    // on screen; they're read in the background, nearest the middle first, and kept until the world, dimension or
    // height cut changes. Regions the game saves while Live is on are read again
    public class SpawnSpots
    {
        // Regions kept read; past this, the ones read longest ago go
        public const int Kept = 96;
        // Farther out than this many regions on screen, nothing is drawn, rather than reading half the world
        public const int MostRegions = 64;

        public Scene Scene { get; }

        // Raised on the UI thread when a region has been read
        public event Action? Changed;

        private readonly Dictionary<Coords, SpawnRegion> _read = [];
        private readonly LinkedList<Coords> _age = new();
        private readonly HashSet<Coords> _stale = [];
        private List<Coords> _wanted = [];
        // Being read now, so not asked for again meanwhile
        private readonly HashSet<Coords> _reading = [];
        private (DimensionDomain? Dimension, int? StartY) _key;
        private int _generation;
        // The generation the reader running belongs to, if one is
        private int? _runner;
        private readonly object _gate = new();

        public SpawnSpots(Scene scene, LiveReloadTool live)
        {
            Scene = scene;
            Scene.WorldChanged += (old, current) => Clear();
            Scene.DimensionChanged += (old, current) => Clear();
            Scene.ProfileReset += dimension => CheckCut();
            live.Refreshed += (dimension, regions) =>
            {
                if (dimension != _key.Dimension) return;
                foreach (Coords region in regions)
                {
                    if (_read.ContainsKey(region)) _stale.Add(region);
                }
                Changed?.Invoke();
            };
        }

        // The regions the screen shows, nearest its middle first; null when there are too many to draw
        public static List<Coords>? OnScreen(IScene scene)
        {
            XzPoint topLeft = scene.TopLeft, bottomRight = scene.BottomRight;
            int left = Region(topLeft.X), top = Region(topLeft.Z), right = Region(bottomRight.X), bottom = Region(bottomRight.Z);
            if ((long)(right - left + 1) * (bottom - top + 1) > MostRegions) return null;

            double middleX = (topLeft.X + bottomRight.X) / 2 / SpawnRegion.Size - 0.5, middleZ = (topLeft.Z + bottomRight.Z) / 2 / SpawnRegion.Size - 0.5;
            List<Coords> output = [];
            for (int z = top; z <= bottom; z++)
            {
                for (int x = left; x <= right; x++) output.Add(new Coords(x, z));
            }
            return [.. output.OrderBy(c => (c.X - middleX) * (c.X - middleX) + (c.Z - middleZ) * (c.Z - middleZ))];

            static int Region(double block) => MathUtilities.FindSectionY((int)Math.Floor(block), SpawnRegion.Size);
        }

        // Every region read so far; UI thread
        public IEnumerable<SpawnRegion> Regions => _read.Values;

        // What's been read of a region, or null; UI thread
        public SpawnRegion? Get(Coords region) => _read.TryGetValue(region, out SpawnRegion? read) ? read : null;

        // The regions on screen, nearest the middle first; those not read yet are read, and the rest wait
        public void Want(IReadOnlyList<Coords> regions)
        {
            CheckCut();
            if (_key.Dimension is not DimensionDomain dimension) return;

            lock (_gate)
            {
                List<Coords> wanted = [.. regions.Where(r => (!_read.ContainsKey(r) || _stale.Contains(r)) && !_reading.Contains(r))];
                _wanted = wanted;
                if (_runner == _generation || wanted.Count == 0) return;
                _runner = _generation;
            }

            int generation = _generation;
            SceneInfo scene = dimension.Scene.SceneParameter;
            int? startY = _key.StartY;
            _ = Task.Run(() => Read(generation, scene, startY));
        }

        private void Read(int generation, SceneInfo scene, int? startY)
        {
            HashSet<Coords> exist;
            try
            {
                exist = [.. scene.Regions];
            }
            catch (Exception e)
            {
                Console.WriteLine($"Spawn spots: {e.Message}");
                Stopped(generation);
                return;
            }

            SpawnScanner scanner = new(startY);
            while (true)
            {
                // Two at a time, as the scanner reads them side by side
                List<Coords> batch;
                lock (_gate)
                {
                    if (generation != _generation || _wanted.Count == 0)
                    {
                        if (_runner == generation) _runner = null;
                        return;
                    }
                    batch = [.. _wanted.Take(2)];
                    _wanted.RemoveRange(0, batch.Count);
                    _reading.UnionWith(batch);
                }

                List<Coords> toRead = [.. batch.Where(exist.Contains)];
                try
                {
                    if (toRead.Count > 0) scanner.Scan(scene.RegionStore, toRead, read => Post(() => Done(generation, read)));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Spawn spots: {e.Message}");
                }
                // Regions with nothing saved are kept as empty, so they aren't asked for again
                foreach (Coords empty in batch.Except(toRead)) Post(() => Done(generation, new SpawnRegion(empty)));
                lock (_gate) _reading.ExceptWith(batch);
            }
        }

        private void Done(int generation, SpawnRegion region)
        {
            if (generation != _generation) return;
            Keep(region);
            Changed?.Invoke();
        }

        private void Stopped(int generation)
        {
            lock (_gate)
            {
                if (_runner == generation) _runner = null;
            }
        }

        private void Keep(SpawnRegion region)
        {
            _read[region.Region] = region;
            _stale.Remove(region.Region);
            _age.Remove(region.Region);
            _age.AddLast(region.Region);
            while (_age.Count > Kept && _age.First is LinkedListNode<Coords> oldest)
            {
                _read.Remove(oldest.Value);
                _age.RemoveFirst();
            }
        }

        // The height panel moved the cut, so the floors are other ones
        private void CheckCut()
        {
            if (Scene.Domain.CurrentWorld is not WorldDomain world)
            {
                if (_key.Dimension is not null) Clear();
                return;
            }
            DimensionDomain dimension = world.CurrentDimension;
            (int? startY, _) = Scene.GetScanStart(world, dimension);
            if (_key != (dimension, startY)) Clear();
        }

        public void Clear()
        {
            lock (_gate)
            {
                _generation++;
                _wanted = [];
                _reading.Clear();
            }
            _read.Clear();
            _age.Clear();
            _stale.Clear();
            _key = Scene.Domain.CurrentWorld is WorldDomain world ? (world.CurrentDimension, Scene.GetScanStart(world, world.CurrentDimension).StartY) : (null, null);
            Changed?.Invoke();
        }

        private static void Post(Action action)
        {
#if AVALONIA
            Dispatcher.UIThread.Post(action);
#else
            Application.Current?.Dispatcher.BeginInvoke(action);
#endif
        }
    }
}
