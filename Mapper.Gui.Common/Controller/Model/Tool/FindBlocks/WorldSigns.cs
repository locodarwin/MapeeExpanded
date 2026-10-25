using Mapper.Gui.Logic;
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
    // The signs of the world on show, for the find window's Signs list. A dimension is read in the background when
    // the list wants it, or when it has signs shown on the map, whose words are then brought up to date and which are
    // taken off when the sign is gone. Kept until another world opens; the regions Live redraws are read again
    public class WorldSigns
    {
        public Scene Scene { get; }
        public MarkupTool Markup { get; }

        // While the Signs list is open; Live's saves are read again then too
        public bool Listed { get; set; }

        // Raised on the UI thread when a dimension has been read, or a read starts
        public event Action? Changed;

        private readonly Dictionary<Dimension, DimensionSigns> _read = [];
        // Regions saved since each dimension was read
        private readonly Dictionary<Dimension, HashSet<Coords>> _stale = [];
        private Dimension? _reading;
        private WorldDomain? _world;
        private CancellationTokenSource? _cancel;

        public WorldSigns(Scene scene, MarkupTool markup, LiveReloadTool live)
        {
            Scene = scene;
            Markup = markup;

            // After the markup tool has read the world's markup, which has the signs shown
            Scene.WorldChanged += (old, current) =>
            {
                Forget();
                Post(WantHere);
            };
            Scene.DimensionChanged += (old, current) => Post(() =>
            {
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
        public DimensionSigns? Current =>
            Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension dimension && _read.TryGetValue(dimension, out DimensionSigns? found) ? found : null;

        public bool CanShow => Markup.Enabled;

        public bool IsShown(Sign sign) => Markup.Editor.Document.SignAt(sign.Dimension.Namespace, sign.X, sign.Y, sign.Z) is not null;

        public int ShownHere => Markup.Editor.Document.SignsIn(Markup.View.Dimension).Count();

        // Ticked in the list: on the map, with the rest of the world's markup, which is shown if it was hidden
        public void Show(IEnumerable<Sign> signs)
        {
            if (!CanShow) return;
            Markup.ShowMarkup = true;
            if (Markup.Editor.ShowSigns(signs) > 0) Changed?.Invoke();
        }

        public void Hide(IEnumerable<Sign> signs)
        {
            MarkupDocument document = Markup.Editor.Document;
            List<MapSign> shown = [.. signs.Select(s => document.SignAt(s.Dimension.Namespace, s.X, s.Y, s.Z)).OfType<MapSign>()];
            if (Markup.Editor.HideSigns(shown) > 0) Changed?.Invoke();
        }

        // Every sign shown in the dimension on show taken off the map
        public void HideAll()
        {
            if (Markup.Editor.HideSigns([.. Markup.Editor.Document.SignsIn(Markup.View.Dimension)]) > 0) Changed?.Invoke();
        }

        // A pin where the sign is, labelled with its first line
        public void Pin(Sign sign)
        {
            if (!Markup.Enabled) return;
            Markup.ShowMarkup = true;
            Markup.Editor.Import(new MarkupDocument() { Pins = [new MapPin() { X = sign.X, Z = sign.Z, Dimension = sign.Dimension.Namespace, Label = sign.Title, Icon = "flag" }] });
        }

        // Reads the dimension on show unless it's read and up to date
        public void Want()
        {
            if (Reading || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            DimensionDomain dimension = world.CurrentDimension;
            DimensionSigns? earlier = _read.GetValueOrDefault(dimension.Dimension);
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
                DimensionSigns? found = null;
                try
                {
                    found = SignMap.Update(earlier, scene, stale, cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Signs in the {scene.Dimension.Name}: {e.Message}");
                }

                Post(() =>
                {
                    if (cancel.IsCancellationRequested || _world != world) return;
                    _reading = null;
                    // One that can't be read is left as it was, or empty, rather than tried again over and over;
                    // the signs shown stay as they were
                    _read[scene.Dimension] = found ?? earlier ?? SignMap.None(scene.Dimension);
                    if (found is not null) Markup.Editor.UpdateSigns(found);
                    Changed?.Invoke();
                    // Another dimension shown, or saves, while this one was read
                    WantHere();
                });
            });
        }

        // Read while the list is open, or when the dimension on show has signs on the map, which should say what
        // they say now
        private void WantHere()
        {
            if (Listed || ShownHere > 0) Want();
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
