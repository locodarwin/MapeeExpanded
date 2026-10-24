using Mapper.Gui.Logic;
using System;
using System.Collections.Generic;
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
    // The structures of the world on show, for the structure overlay of either GUI and Go to. Each dimension is read
    // in the background the first time it's shown with the overlay on, and kept until another world opens; the
    // regions Live redraws are read again when it's next shown
    public class WorldStructures
    {
        public Scene Scene { get; }

        // While the overlay is on; Go to lists the structures then
        public bool Shown { get; set; }

        // Raised on the UI thread when a dimension has been read
        public event Action? Changed;

        private readonly Dictionary<Dimension, DimensionStructures> _read = [];
        // Regions saved since each dimension was read
        private readonly Dictionary<Dimension, HashSet<Coords>> _stale = [];
        private Dimension? _reading;
        private WorldDomain? _world;
        private CancellationTokenSource? _cancel;

        public WorldStructures(Scene scene, LiveReloadTool live)
        {
            Scene = scene;
            Scene.WorldChanged += (old, current) => Forget();
            live.Refreshed += (dimension, regions) =>
            {
                // Saves during a first read count too: it may have read those regions already
                if (!_read.ContainsKey(dimension.Dimension) && _reading != dimension.Dimension) return;
                if (!_stale.TryGetValue(dimension.Dimension, out HashSet<Coords>? stale)) _stale[dimension.Dimension] = stale = [];
                stale.UnionWith(regions);
                Changed?.Invoke();
            };
        }

        public bool Reading => _reading is not null;

        // The dimension on show's, as far as read; UI thread
        public IReadOnlyList<Structure> Current =>
            Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension dimension && _read.TryGetValue(dimension, out DimensionStructures? found) ? found.All : [];

        // The overlay is on: reads the dimension on show unless it's read and up to date
        public void Want()
        {
            if (Reading || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            DimensionDomain dimension = world.CurrentDimension;
            DimensionStructures? earlier = _read.GetValueOrDefault(dimension.Dimension);
            HashSet<Coords>? stale = _stale.GetValueOrDefault(dimension.Dimension);
            if (earlier is not null && stale is null) return;

            _cancel?.Cancel();
            CancellationTokenSource cancel = _cancel = new CancellationTokenSource();
            _world = world;
            _reading = dimension.Dimension;
            _stale.Remove(dimension.Dimension);
            SceneInfo scene = dimension.Scene.SceneParameter;

            _ = Task.Run(() =>
            {
                DimensionStructures? found = null;
                try
                {
                    found = StructureMap.Update(earlier, scene, stale, cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Structures in the {scene.Dimension.Name}: {e.Message}");
                }

                Post(() =>
                {
                    if (cancel.IsCancellationRequested || _world != world) return;
                    _reading = null;
                    // One that can't be read is left as it was, or empty, rather than tried again on every paint
                    _read[scene.Dimension] = found ?? earlier ?? StructureMap.None(scene.Dimension);
                    Changed?.Invoke();
                });
            });
        }

        private void Forget()
        {
            _cancel?.Cancel();
            _cancel = null;
            _world = null;
            _reading = null;
            _read.Clear();
            _stale.Clear();
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
