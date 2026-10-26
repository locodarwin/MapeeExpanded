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
    // The biomes of the world on show, for the find window's Biomes list. A dimension is read in the background when
    // the list wants it, and kept until another world opens; the regions Live redraws are read again. While the list
    // is open, the patches it names are shaded on the map (FindPainter), the one picked more strongly
    public class WorldBiomes
    {
        public Scene Scene { get; }
        public MarkupTool Markup { get; }

        // While the Biomes list is open; nothing is shaded otherwise
        public bool Listed
        {
            get => _listed;
            set
            {
                if (_listed == value) return;
                _listed = value;
                _renderer.Render();
            }
        }

        // What the list shades: every patch it lists when it's been asked for a biome by name, and the one picked
        public IReadOnlyList<BiomePatch> Shaded { get; private set; } = [];
        public BiomePatch? Picked { get; private set; }

        // Raised on the UI thread when a dimension has been read, or a read starts
        public event Action? Changed;

        private readonly IRenderInvoker _renderer;
        private readonly Dictionary<Dimension, DimensionBiomes> _read = [];
        // Regions saved since each dimension was read
        private readonly Dictionary<Dimension, HashSet<Coords>> _stale = [];
        private Dimension? _reading;
        private WorldDomain? _world;
        private CancellationTokenSource? _cancel;
        private bool _listed;

        public WorldBiomes(Scene scene, MarkupTool markup, LiveReloadTool live, IRenderInvoker renderer)
        {
            Scene = scene;
            Markup = markup;
            _renderer = renderer;

            Scene.WorldChanged += (old, current) =>
            {
                Forget();
                Post(WantHere);
            };
            Scene.DimensionChanged += (old, current) => Post(() =>
            {
                Shade([], null);
                WantHere();
                Changed?.Invoke();
            });
            live.Refreshed += (dimension, regions) =>
            {
                // Saves during a first read count too: it may have read those regions already
                if (!_read.ContainsKey(dimension.Dimension) && _reading != dimension.Dimension) return;
                if (!_stale.TryGetValue(dimension.Dimension, out HashSet<Coords>? stale)) _stale[dimension.Dimension] = stale = [];
                stale.UnionWith(regions);
                WantHere();
            };
        }

        public bool Reading => _reading is not null;

        // The dimension on show's, as far as read, or null before it is; UI thread
        public DimensionBiomes? Current =>
            Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension dimension && _read.TryGetValue(dimension, out DimensionBiomes? found) ? found : null;

        // What the map shades, redrawn when it changes
        public void Shade(IReadOnlyList<BiomePatch> shaded, BiomePatch? picked)
        {
            if (ReferenceEquals(shaded, Shaded) && picked == Picked) return;
            if (shaded.Count == 0 && Shaded.Count == 0 && picked == Picked) return;
            Shaded = shaded;
            Picked = picked;
            if (Listed) _renderer.Render();
        }

        // A pin in the middle of a patch, labelled with its biome
        public void Pin(BiomePatch patch)
        {
            if (!Markup.Enabled) return;
            Markup.ShowMarkup = true;
            Markup.Editor.Import(new MarkupDocument() { Pins = [new MapPin() { X = patch.X, Z = patch.Z, Dimension = patch.Dimension.Namespace, Label = patch.Name, Icon = "star" }] });
        }

        // Reads the dimension on show unless it's read and up to date
        public void Want()
        {
            if (Reading || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            DimensionDomain dimension = world.CurrentDimension;
            DimensionBiomes? earlier = _read.GetValueOrDefault(dimension.Dimension);
            HashSet<Coords>? stale = _stale.GetValueOrDefault(dimension.Dimension);
            if (earlier is not null && stale is null) return;

            _cancel?.Cancel();
            CancellationTokenSource cancel = _cancel = new CancellationTokenSource();
            _world = world;
            _reading = dimension.Dimension;
            _stale.Remove(dimension.Dimension);
            SceneInfo scene = dimension.Scene.SceneParameter;
            Changed?.Invoke();

            _ = Task.Run(() =>
            {
                DimensionBiomes? found = null;
                try
                {
                    // A Bedrock world still being indexed gives its regions once it's done
                    if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancel.Token);
                    found = BiomeMap.Update(earlier, scene, stale, cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Biomes in the {scene.Dimension.Name}: {e.Message}");
                }

                Post(() =>
                {
                    if (cancel.IsCancellationRequested || _world != world) return;
                    _reading = null;
                    // One that can't be read is left as it was, or empty, rather than tried again over and over
                    _read[scene.Dimension] = found ?? earlier ?? BiomeMap.None(scene.Dimension);
                    Changed?.Invoke();
                    // Another dimension shown, or saves, while this one was read
                    WantHere();
                });
            });
        }

        private void WantHere()
        {
            if (Listed) Want();
        }

        private void Forget()
        {
            _cancel?.Cancel();
            _cancel = null;
            _world = null;
            _reading = null;
            _read.Clear();
            _stale.Clear();
            Shade([], null);
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
