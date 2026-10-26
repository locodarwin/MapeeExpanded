using Mapper.Gui.Controller;
using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
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
    // A patch of a biome in the Biomes list, with its part nearest where the list was sorted from. Count is how many
    // patches the biome has, for a row standing for all of them
    public class BiomeRow(BiomePatch patch, int x, int z, double distance, int count)
    {
        public BiomePatch Patch { get; } = patch;
        // Its part nearest the map's middle when sorted, where Go there goes
        public int X { get; } = x;
        public int Z { get; } = z;

        public string Title => Patch.Name;
        public string More => $"{Patch.Width.ToString("N0", CultureInfo.CurrentCulture)} × {Patch.Depth.ToString("N0", CultureInfo.CurrentCulture)} blocks"
            + (count > 1 ? $", the nearest of {count.ToString("N0", CultureInfo.CurrentCulture)}" : "");
        public string Place => $"{X}, {Z}";
        public string Distance => distance < 1 ? "here" : $"{distance.ToString("N0", CultureInfo.CurrentCulture)} away";
        public string ToolTip => $"{Patch.Name}\n{More}\nThe nearest part is at {Place}; its middle is at {Patch.X}, {Patch.Z}";
    }

    // What the find window's Biomes list shows and does, apart from its layout, which each GUI has its own of. With the
    // box empty it lists each biome once, by its nearest patch; with words in it, every patch of the biomes they name,
    // which the map then shades
    public class BiomesPanel
    {
        public WorldBiomes Biomes { get; }
        public CanvasControl Canvas { get; }

        public XzPoint Sorted { get; private set; }

        private string[] _words = [];
        private DimensionBiomes? _listed;
        private List<BiomeRow> _rows = [];

        public BiomesPanel(WorldBiomes biomes, CanvasControl canvas)
        {
            Biomes = biomes;
            Canvas = canvas;
        }

        public bool Named => _words.Length > 0;

        // Biomes whose name has every word typed
        public void Filter(string text)
        {
            _words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _listed = null;
        }

        // Lists them again nearest the middle of the map, from where it is now
        public void SortFromHere() => _listed = null;

        // The rows, made again when what's read, the filter or the sorting has changed since; otherwise the same list
        public IReadOnlyList<BiomeRow> Rows()
        {
            DimensionBiomes? current = Biomes.Current;
            if (current is null)
            {
                _listed = null;
                _rows = [];
                Biomes.Shade([], null);
                return _rows;
            }
            if (ReferenceEquals(current, _listed)) return _rows;

            _listed = current;
            Sorted = Biomes.Scene.Map.TransformPointOnScreenToXz(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
            var nearest = DimensionBiomes.ByDistance(current.Patches.Where(p => BiomeText.Says(p.Biome, _words)), Sorted.X, Sorted.Z);
            if (Named)
            {
                _rows = [.. nearest.Select(n => new BiomeRow(n.Patch, n.X, n.Z, n.Distance, 0))];
            }
            else
            {
                Dictionary<string, int> counts = current.Patches.GroupBy(p => p.Biome).ToDictionary(g => g.Key, g => g.Count());
                _rows = [.. nearest.DistinctBy(n => n.Patch.Biome).Select(n => new BiomeRow(n.Patch, n.X, n.Z, n.Distance, counts[n.Patch.Biome]))];
            }
            Biomes.Shade(Named ? [.. _rows.Select(r => r.Patch)] : [], Biomes.Picked is BiomePatch picked && _rows.Any(r => r.Patch == picked) ? picked : null);
            return _rows;
        }

        public IReadOnlyList<BiomeRow> Listed => _rows;

        // Picked in the list: shaded more strongly
        public void Pick(BiomeRow? row) => Biomes.Shade(Biomes.Shaded, row?.Patch);

        // One line on what's read and listed
        public string Status()
        {
            if (Biomes.Scene.Domain.CurrentWorld is not WorldDomain world) return "Open a world to list its biomes";
            string where = The(world.CurrentDimension.Dimension.Name);
            DimensionBiomes? current = Biomes.Current;
            if (current is null) return Biomes.Reading ? $"Reading the biomes of {where}" : string.Empty;
            if (current.Patches.Count == 0) return $"No biomes found in {where}";

            string reading = Biomes.Reading ? ". Reading saves" : "";
            if (!Named) return $"{Count(current.Biomes.Count, "biome")} in {where}, each listed by its nearest patch. Type a name to list and shade every patch of it{reading}";

            int biomes = _rows.Select(r => r.Patch.Biome).Distinct().Count();
            string words = string.Join(" ", _words);
            return biomes == 0
                ? $"No biome in {where} is called “{words}”{reading}"
                : $"{Count(_rows.Count, "patch")} of {(biomes == 1 ? _rows[0].Patch.Name : Count(biomes, "biome"))} in {where}, shaded on the map{reading}";
        }

        public string SortedText() => _rows.Count == 0 ? string.Empty : $"Nearest {Sorted.X:0}, {Sorted.Z:0} first";

        private static string Count(int count, string what) => $"{count.ToString("N0", CultureInfo.CurrentCulture)} {what}{(count == 1 ? "" : what.EndsWith("ch") ? "es" : "s")}";

        // "the Overworld", "The End"
        private static string The(string dimension) => dimension.StartsWith("The ") ? dimension : $"the {dimension}";
    }
}
