using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Mapper.Gui
{
    // A block in the find window's list: the name the map shows, and the id beside it
    public class FindBlockRow(string name)
    {
        public string Name { get; } = name;
        public string Title { get; } = ColumnInspector.DisplayName(name);
        public string Id { get; } = name.StartsWith("minecraft:") ? name["minecraft:".Length..] : name;
        public override string ToString() => Title;
    }

    // Something chosen to look for, in the colour it's marked in
    public class FindChip(FindTarget target, int index)
    {
        public FindTarget Target { get; } = target;
        public string Label => Target.Label;
        public string Color { get; } = FindColors.Of(index);
        public string ToolTip { get; } = string.Join(", ", target.Blocks.Select(ColumnInspector.DisplayName));
    }

    // A vein in the results list. Its text is worked out when it's shown, so a long list costs little
    public class FindResultRow(FindTool tool, FoundCluster cluster)
    {
        public FoundCluster Cluster { get; } = cluster;
        public string Color => FindColors.Of(Cluster.Target);
        public string Title => tool.Label(Cluster);
        public string Place => $"{Cluster.X}, {Cluster.Y}, {Cluster.Z}";
        public string Distance
        {
            get
            {
                double blocks = FindResults.Distance(Cluster, tool.Sorted.X, tool.Sorted.Z);
                return blocks < 1 ? "here" : $"{blocks.ToString("N0", CultureInfo.CurrentCulture)} away";
            }
        }
    }

    // What the find window shows and does, apart from its layout, which each GUI has its own of
    public class FindPanel
    {
        // Each has its own colour, and past eight they'd repeat
        public const int MaxTargets = 8;

        public FindTool Tool { get; }

        public ObservableCollection<FindBlockRow> Blocks { get; } = [];
        public ObservableCollection<FindChip> Chips { get; } = [];
        public IReadOnlyList<FindTarget> Groups => FindGroups.All;

        private readonly List<FindBlockRow> _allBlocks;
        private readonly List<FindTarget> _targets = [];

        public FindPanel(FindTool tool)
        {
            Tool = tool;
            _allBlocks = [.. KnownBlocks.Names.Select(n => new FindBlockRow(n)).OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)];
            Filter(string.Empty);

            // What was looked for last stays chosen when the window opens again
            if (tool.LastQuery is BlockQuery last) foreach (FindTarget target in last.Targets) Add(target);
        }

        public IReadOnlyList<FindTarget> Targets => _targets;

        // Blocks whose name or id holds every word typed
        public void Filter(string text)
        {
            string[] words = text.Split(' ', '_', StringSplitOptions.RemoveEmptyEntries);
            Blocks.Clear();
            foreach (FindBlockRow block in _allBlocks)
            {
                if (words.All(w => block.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) || block.Id.Contains(w, StringComparison.OrdinalIgnoreCase))) Blocks.Add(block);
            }
        }

        // Returns what to tell the user if it can't be added
        public string? Add(FindBlockRow block) => Add(FindTarget.Of(block.Name));

        public string? Add(FindTarget target)
        {
            if (_targets.Any(t => t.Label == target.Label && t.Blocks.SequenceEqual(target.Blocks))) return null;
            if (_targets.Count >= MaxTargets) return $"Up to {MaxTargets} at once; take one off first";
            _targets.Add(target);
            ShowChips();
            return null;
        }

        public void Remove(FindChip chip)
        {
            _targets.Remove(chip.Target);
            ShowChips();
        }

        private void ShowChips()
        {
            Chips.Clear();
            for (int i = 0; i < _targets.Count; i++) Chips.Add(new FindChip(_targets[i], i));
        }

        // The query from what's chosen and the height boxes (blank for no limit), or what's wrong with them
        public BlockQuery? Query(string? from, string? to, out string? problem)
        {
            problem = null;
            if (_targets.Count == 0)
            {
                problem = "Pick a block or a group to look for";
                return null;
            }
            if (!Height(from, out int? minY) || !Height(to, out int? maxY))
            {
                problem = "Heights are whole numbers, such as -64 or 16; leave them blank for all heights";
                return null;
            }
            if (minY is int low && maxY is int high && low > high) (minY, maxY) = (high, low);
            return new BlockQuery([.. _targets], minY, maxY);
        }

        private static bool Height(string? text, out int? y)
        {
            y = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value)) return false;
            y = value;
            return true;
        }

        // One line on how the search is going, or what it found
        public string Status()
        {
            if (Tool.Problem is string problem) return $"The search stopped: {problem}";
            if (Tool.Results is not FindResults results || Tool.Dimension is null)
            {
                return Tool.Scene.Domain.CurrentWorld is null ? "Open a world to search it" : $"Pick what to look for, then Find to search {The(Tool.Scene.Domain.CurrentWorld.CurrentDimension.Dimension.Name)}";
            }

            string where = The(Tool.Dimension.Dimension.Name);
            int total = results.Total;
            if (Tool.Searching)
            {
                return Tool.RegionsTotal == 0 ? $"Reading {where}" : $"Searching {where}: {Tool.RegionsDone:N0} of {Tool.RegionsTotal:N0} regions, {total:N0} found";
            }
            string stopped = !Tool.Halted ? string.Empty
                : Tool.RegionsTotal == 0 ? "Stopped while the world was still being read. "
                : $"Stopped after {Tool.RegionsDone:N0} of {Tool.RegionsTotal:N0} regions. ";
            if (total == 0) return $"{stopped}None in {where}{Heights(results.Query)}";

            string found = $"{stopped}Found {total:N0} in {Tool.Clusters.Count:N0} {(Tool.Clusters.Count == 1 ? "place" : "places")} in {where}{Heights(results.Query)}";
            if (Tool.Stopped) found += $". Stopped at {BlockFinder.MaxMatches:N0}: look for something rarer, or narrow the heights";
            return found;
        }

        // How many of each were found, once a search has finished
        public string Counts()
        {
            if (Tool.Results is not FindResults results || Tool.Searching || results.Query.Targets.Count < 2) return string.Empty;
            IReadOnlyList<int> counts = results.Counts;
            return string.Join("   ", results.Query.Targets.Select((t, i) => $"{t.Label} {counts[i]:N0}"));
        }

        public string SortedText() => Tool.Clusters.Count == 0 ? string.Empty : $"Nearest {Tool.Sorted.X:0}, {Tool.Sorted.Z:0} first";

        public IReadOnlyList<FindResultRow> Rows() => [.. Tool.Clusters.Select(c => new FindResultRow(Tool, c))];

        private static string Heights(BlockQuery query) => (query.MinY, query.MaxY) switch
        {
            (null, null) => string.Empty,
            (int min, null) => $" from Y {min} up",
            (null, int max) => $" up to Y {max}",
            (int min, int max) => $" between Y {min} and {max}"
        };

        // "the Overworld", "The End"
        private static string The(string dimension) => dimension.StartsWith("The ") ? dimension : $"the {dimension}";
    }
}
