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
    // The named mobs of the world on show, for the find window's Pets list. A dimension is read in the background when
    // the list wants it, or when it has mobs shown on the map, which then move to where they were saved and are taken
    // off when they're gone. Kept until another world opens; the regions Live redraws are read again
    public class WorldPets
    {
        public Scene Scene { get; }
        public MarkupTool Markup { get; }

        // While the Pets list is open; Live's saves are read again then too
        public bool Listed { get; set; }

        // Raised on the UI thread when a dimension has been read, or a read starts
        public event Action? Changed;

        private readonly Dictionary<Dimension, DimensionPets> _read = [];
        // Regions saved since each dimension was read
        private readonly Dictionary<Dimension, HashSet<Coords>> _stale = [];
        private Dimension? _reading;
        private WorldDomain? _world;
        private CancellationTokenSource? _cancel;

        public WorldPets(Scene scene, MarkupTool markup, LiveReloadTool live)
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
        public DimensionPets? Current =>
            Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension dimension && _read.TryGetValue(dimension, out DimensionPets? found) ? found : null;

        public bool CanShow => Markup.Enabled;

        public bool IsShown(Pet pet) => Markup.Editor.Document.PetById(pet.Id) is not null;

        public int ShownHere => Markup.Editor.Document.PetsIn(Markup.View.Dimension).Count();

        // Ticked in the list: on the map, with the rest of the world's markup, which is shown if it was hidden
        public void Show(IEnumerable<Pet> pets)
        {
            if (!CanShow) return;
            Markup.ShowMarkup = true;
            if (Markup.Editor.ShowPets(pets) > 0) Changed?.Invoke();
        }

        public void Hide(IEnumerable<Pet> pets)
        {
            MarkupDocument document = Markup.Editor.Document;
            List<MapPet> shown = [.. pets.Select(p => document.PetById(p.Id)).OfType<MapPet>()];
            if (Markup.Editor.HidePets(shown) > 0) Changed?.Invoke();
        }

        // Every mob shown in the dimension on show taken off the map
        public void HideAll()
        {
            if (Markup.Editor.HidePets([.. Markup.Editor.Document.PetsIn(Markup.View.Dimension)]) > 0) Changed?.Invoke();
        }

        // A pin where the mob was, labelled with its name
        public void Pin(Pet pet)
        {
            if (!Markup.Enabled) return;
            Markup.ShowMarkup = true;
            Markup.Editor.Import(new MarkupDocument() { Pins = [new MapPin() { X = pet.BlockX, Z = pet.BlockZ, Dimension = pet.Dimension.Namespace, Label = pet.Name, Icon = "star" }] });
        }

        // Reads the dimension on show unless it's read and up to date
        public void Want()
        {
            if (Reading || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            DimensionDomain dimension = world.CurrentDimension;
            DimensionPets? earlier = _read.GetValueOrDefault(dimension.Dimension);
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
                DimensionPets? found = null;
                try
                {
                    found = PetMap.Update(earlier, scene, stale, cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Named mobs in the {scene.Dimension.Name}: {e.Message}");
                }

                Post(() =>
                {
                    if (cancel.IsCancellationRequested || _world != world) return;
                    _reading = null;
                    // One that can't be read is left as it was, or empty, rather than tried again over and over;
                    // the mobs shown stay as they were
                    _read[scene.Dimension] = found ?? earlier ?? PetMap.None(scene.Dimension);
                    if (found is not null) Markup.Editor.UpdatePets(found);
                    Changed?.Invoke();
                    // Another dimension shown, or saves, while this one was read
                    WantHere();
                });
            });
        }

        // Read while the list is open, or when the dimension on show has mobs on the map, which should be where they
        // are now
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
