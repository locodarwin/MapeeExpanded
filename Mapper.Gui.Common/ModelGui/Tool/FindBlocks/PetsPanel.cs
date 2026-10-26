using Mapper.Gui.Controller;
using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using WorldEditor;
#if AVALONIA
using Avalonia;
#else
using System.Windows;
#endif

namespace Mapper.Gui
{
    // A named mob in the Pets list. Its tick says whether it's shown on the map, and changes that
    public class PetRow(WorldPets pets, Pet pet, XzPoint from) : INotifyPropertyChanged
    {
        public Pet Pet { get; } = pet;

        public string Title => Pet.Name;
        public string More => Pet.Tamed ? $"Tamed {Pet.Kind.ToLowerInvariant()}" : Pet.Kind;
        public string Place => $"{Pet.BlockX}, {Pet.BlockY}, {Pet.BlockZ}";
        public string ToolTip => $"{Pet.Name}\n{More}\nWhere the game last saved it";

        public string Distance
        {
            get
            {
                double blocks = Math.Sqrt((Pet.X - from.X) * (Pet.X - from.X) + (Pet.Z - from.Z) * (Pet.Z - from.Z));
                return blocks < 1 ? "here" : $"{blocks.ToString("N0", CultureInfo.CurrentCulture)} away";
            }
        }

        public bool CanShow => pets.CanShow;

        public bool Shown
        {
            get => pets.IsShown(Pet);
            set
            {
                if (value == Shown) return;
                if (value) pets.Show([Pet]);
                else pets.Hide([Pet]);
                Changed();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown)));
    }

    // What the find window's Pets list shows and does, apart from its layout, which each GUI has its own of
    public class PetsPanel
    {
        public WorldPets Pets { get; }
        public CanvasControl Canvas { get; }

        public XzPoint Sorted { get; private set; }

        private string[] _words = [];
        private DimensionPets? _listed;
        private List<PetRow> _rows = [];

        public PetsPanel(WorldPets pets, CanvasControl canvas)
        {
            Pets = pets;
            Canvas = canvas;
        }

        // Mobs whose name and kind have every word typed
        public void Filter(string text)
        {
            _words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _listed = null;
        }

        // Lists them again nearest the middle of the map, from where it is now
        public void SortFromHere() => _listed = null;

        // The rows, made again when what's read, the filter or the sorting has changed since; otherwise the same
        // list, each row with its tick brought up to date
        public IReadOnlyList<PetRow> Rows()
        {
            DimensionPets? current = Pets.Current;
            if (current is null) return _rows = [];
            if (ReferenceEquals(current, _listed))
            {
                foreach (PetRow row in _rows) row.Changed();
                return _rows;
            }

            _listed = current;
            Sorted = Pets.Scene.Map.TransformPointOnScreenToXz(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
            XzPoint from = Sorted;
            _rows = [.. current.All
                .Where(p => PetText.Says(p, _words))
                .OrderBy(p => (p.X - from.X) * (p.X - from.X) + (p.Z - from.Z) * (p.Z - from.Z))
                .Select(p => new PetRow(Pets, p, from))];
            return _rows;
        }

        public IReadOnlyList<PetRow> Listed => _rows;

        // One line on what's read and shown
        public string Status()
        {
            if (Pets.Scene.Domain.CurrentWorld is not WorldDomain world) return "Open a world to list its named mobs";
            string where = The(world.CurrentDimension.Dimension.Name);
            DimensionPets? current = Pets.Current;
            if (current is null) return Pets.Reading ? $"Looking for named mobs in {where}" : string.Empty;

            int total = current.All.Count;
            if (total == 0) return $"No named mobs in {where}. A mob shows here once it has a name tag";

            string listed = _words.Length > 0 ? $"{_rows.Count:N0} of {Count(total)} in {where} match “{string.Join(" ", _words)}”" : $"{Count(total)} in {where}";
            if (!Pets.CanShow) return $"{listed}. This world can't keep markup, so they can't be shown on the map";
            int shown = Pets.ShownHere;
            return $"{listed}; {(shown == 0 ? "none" : shown.ToString("N0", CultureInfo.CurrentCulture))} shown on the map{(Pets.Reading ? ". Reading saves" : "")}";
        }

        public string SortedText() => _rows.Count == 0 ? string.Empty : $"Nearest {Sorted.X:0}, {Sorted.Z:0} first";

        public void ShowListed() => Pets.Show(_rows.Select(r => r.Pet));

        public void HideAll() => Pets.HideAll();

        private static string Count(int count) => $"{count:N0} named {(count == 1 ? "mob" : "mobs")}";

        // "the Overworld", "The End"
        private static string The(string dimension) => dimension.StartsWith("The ") ? dimension : $"the {dimension}";
    }
}
