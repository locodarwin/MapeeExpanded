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
    // A sign in the Signs list. Its tick says whether it's shown on the map, and changes that
    public class SignRow(WorldSigns signs, Sign sign, XzPoint from) : INotifyPropertyChanged
    {
        public Sign Sign { get; } = sign;

        public string Title => Sign.Title.Length > 0 ? Sign.Title : "Blank sign";
        // The rest of what it says, after the first line; the back after a slash
        public string More
        {
            get
            {
                List<string> front = [.. Sign.Front.Select(l => l.Trim()).Where(l => l.Length > 0)];
                List<string> back = [.. Sign.Back.Select(l => l.Trim()).Where(l => l.Length > 0)];
                if (front.Count == 0) return string.Join(" ", back.Skip(1));
                string rest = string.Join(" ", front.Skip(1));
                if (back.Count == 0) return rest;
                return rest.Length == 0 ? $"/ {string.Join(" ", back)}" : $"{rest} / {string.Join(" ", back)}";
            }
        }
        public bool HasMore => More.Length > 0;
        public string Place => $"{Sign.Kind}   {Sign.X}, {Sign.Y}, {Sign.Z}";
        public string ToolTip => string.Join("\n", MapSign.Of(Sign).Lines);

        public string Distance
        {
            get
            {
                double blocks = Math.Sqrt((Sign.X + 0.5 - from.X) * (Sign.X + 0.5 - from.X) + (Sign.Z + 0.5 - from.Z) * (Sign.Z + 0.5 - from.Z));
                return blocks < 1 ? "here" : $"{blocks.ToString("N0", CultureInfo.CurrentCulture)} away";
            }
        }

        public bool CanShow => signs.CanShow;

        public bool Shown
        {
            get => signs.IsShown(Sign);
            set
            {
                if (value == Shown) return;
                if (value) signs.Show([Sign]);
                else signs.Hide([Sign]);
                Changed();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown)));
    }

    // What the find window's Signs list shows and does, apart from its layout, which each GUI has its own of
    public class SignsPanel
    {
        public WorldSigns Signs { get; }
        public CanvasControl Canvas { get; }

        public XzPoint Sorted { get; private set; }

        private string[] _words = [];
        private bool _blank;
        private DimensionSigns? _listed;
        private List<SignRow> _rows = [];

        public SignsPanel(WorldSigns signs, CanvasControl canvas)
        {
            Signs = signs;
            Canvas = canvas;
        }

        // Signs saying every word typed, on either side
        public void Filter(string text)
        {
            _words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _listed = null;
        }

        public bool IncludeBlank
        {
            get => _blank;
            set
            {
                _blank = value;
                _listed = null;
            }
        }

        // Lists them again nearest the middle of the map, from where it is now
        public void SortFromHere() => _listed = null;

        // The rows, made again when what's read, the filter or the sorting has changed since; otherwise the same
        // list, each row with its tick brought up to date
        public IReadOnlyList<SignRow> Rows()
        {
            DimensionSigns? current = Signs.Current;
            if (current is null) return _rows = [];
            if (ReferenceEquals(current, _listed))
            {
                foreach (SignRow row in _rows) row.Changed();
                return _rows;
            }

            _listed = current;
            Sorted = Signs.Scene.Map.TransformPointOnScreenToXz(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
            XzPoint from = Sorted;
            _rows = [.. current.All
                .Where(s => (_blank || !s.IsBlank) && SignText.Says(s, _words))
                .OrderBy(s => (s.X + 0.5 - from.X) * (s.X + 0.5 - from.X) + (s.Z + 0.5 - from.Z) * (s.Z + 0.5 - from.Z))
                .Select(s => new SignRow(Signs, s, from))];
            return _rows;
        }

        public IReadOnlyList<SignRow> Listed => _rows;

        // One line on what's read and shown
        public string Status()
        {
            if (Signs.Scene.Domain.CurrentWorld is not WorldDomain world) return "Open a world to list its signs";
            string where = The(world.CurrentDimension.Dimension.Name);
            DimensionSigns? current = Signs.Current;
            if (current is null) return Signs.Reading ? $"Reading the signs in {where}" : string.Empty;

            int total = current.All.Count(s => _blank || !s.IsBlank);
            if (total == 0) return $"No signs{(_blank ? "" : " with anything on them")} in {where}";

            string listed = _words.Length > 0 ? $"{_rows.Count:N0} of {Count(total)} in {where} say “{string.Join(" ", _words)}”" : $"{Count(total)} in {where}";
            if (!Signs.CanShow) return $"{listed}. This world can't keep markup, so they can't be shown on the map";
            int shown = Signs.ShownHere;
            return $"{listed}; {(shown == 0 ? "none" : shown.ToString("N0", CultureInfo.CurrentCulture))} shown on the map{(Signs.Reading ? ". Reading saves" : "")}";
        }

        public string SortedText() => _rows.Count == 0 ? string.Empty : $"Nearest {Sorted.X:0}, {Sorted.Z:0} first";

        public void ShowListed() => Signs.Show(_rows.Select(r => r.Sign));

        public void HideAll() => Signs.HideAll();

        private static string Count(int count) => $"{count:N0} {(count == 1 ? "sign" : "signs")}";

        // "the Overworld", "The End"
        private static string The(string dimension) => dimension.StartsWith("The ") ? dimension : $"the {dimension}";
    }
}
