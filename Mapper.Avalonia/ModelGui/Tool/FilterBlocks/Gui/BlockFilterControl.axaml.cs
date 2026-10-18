using AssetSystem;
using AssetSystem.Block;
using Mapper.Gui.Model;
using MapScanner;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Mapper.Gui
{
    public partial class BlockFilterControl : Window
    {
        public IScanProfile ScanProfile { get; private set; }

        public event EventHandler? BlockFilterUpdated;
        public bool DialogClosed { get; private set; } = true;

        private readonly IDictionary<string, BlockEntry<BlockGrouping>> _solids;
        private readonly IDictionary<string, BlockEntry<BlockGrouping>> _transparent;
        private readonly SearchableBlocks _solidSearchableBlocks, _transparentSearchableBlocks;

        public BlockFilterControl(IScanProfile scanProfile)
        {
            InitializeComponent();

            ScanProfile = scanProfile;

            _solids = Seperate(ScanProfile.Asset, BlockType.StopAtEncounter);
            _transparent = Seperate(ScanProfile.Asset, BlockType.SemiTransparent);

            FuncDataTemplate<BlockEntry<BlockGrouping>> blockTemplate = new((entry, _) => new TextBlock() { Text = entry?.BlockName });

            _solidSearchableBlocks = new SearchableBlocks(_solids.Values.ToList());
            SolidBlockView.ItemTemplate = blockTemplate;
            SolidBlockView.ItemsSource = _solidSearchableBlocks.DisplayedBlockList;

            _transparentSearchableBlocks = new SearchableBlocks(_transparent.Values.ToList());
            TransparentBlockView.ItemTemplate = blockTemplate;
            TransparentBlockView.ItemsSource = _transparentSearchableBlocks.DisplayedBlockList;

            List<string> heightmaps = new List<string>(ScanProfile.AllowedNbtHeightmaps ?? Enumerable.Empty<string>());
            if (!heightmaps.Contains(ScanProfile.HeightmapProfile.NbtHeightmap)) 
            {
                heightmaps.Add(ScanProfile.HeightmapProfile.NbtHeightmap);
            }

            HeightmapComboBox.ItemsSource = heightmaps;
            HeightmapComboBox.SelectedItem = ScanProfile.HeightmapProfile.NbtHeightmap;

            HeightmapRadioButton.IsChecked = ScanProfile.HeightmapProfile.HeightmapType == HeightmapType.NbtHeightmap;
            SetYRadioButton.IsChecked = ScanProfile.HeightmapProfile.HeightmapType == HeightmapType.SetY;
            SetYTextBox.Text = ScanProfile.HeightmapProfile.SetY.ToString();

            SolidBlockSearch.TextChanged += SolidBlockSearch_TextChanged;
            TransparentBlockSearch.TextChanged += TransparentBlockSearch_TextChanged;
            HeightmapRadioButton.IsCheckedChanged += (sender, e) => SetHeightmapAvailability();
            SetYRadioButton.IsCheckedChanged += (sender, e) => SetHeightmapAvailability();

            SetHeightmapAvailability();
        }

        private static IDictionary<string, BlockEntry<BlockGrouping>> Seperate(ITokenizedBlockAsset<BlockGrouping> asset, BlockType type)
        {
            Dictionary<string, BlockEntry<BlockGrouping>> output = new();

            foreach (KeyValuePair<string, BlockEntry<BlockGrouping>> pair in asset.Blocks)
            {
                if (!ContainsSpecificType(pair.Value, type)) continue;

                BlockEntry<BlockGrouping> entry = (BlockEntry<BlockGrouping>) pair.Value.Clone();
                KeepOnlySpecificType(entry, type);

                output.Add(pair.Key, entry);
            }

            return output;
        }
        private static bool ContainsSpecificType(BlockEntry<BlockGrouping> entry, BlockType type)
        {
            if (entry.DefaultValue is not null && entry.DefaultValue.Value.Type == type) return true;

            foreach (PropertyMatcher<BlockGrouping> propertyMatcher in entry.Evaluators)
            {
                if (propertyMatcher.Payload.Type == type) return true;
            }

            return false;
        }
        private static void KeepOnlySpecificType(BlockEntry<BlockGrouping> entry, BlockType type) 
        {
            if (entry.DefaultValue is not null && entry.DefaultValue.Value.Type != type) 
            {
                entry.DefaultValue = null;
            }

            for (int i = 0; i < entry.Evaluators.Count; i++) 
            {
                if (entry.Evaluators[i].Payload.Type != type) 
                {
                    entry.Evaluators.RemoveAt(i--);
                }
            }
        }

        private void SolidBlockSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _solidSearchableBlocks.Search((SolidBlockSearch.Text ?? string.Empty).ToLower());
        }
        private void TransparentBlockSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _transparentSearchableBlocks.Search((TransparentBlockSearch.Text ?? string.Empty).ToLower());
        }

        private void SolidBlockRemoveButton_Click(object? sender, RoutedEventArgs e)
        {
            _solidSearchableBlocks.Remove(SelectedBlocks(SolidBlockView), BlockType.StopAtEncounter);
        }
        private void TransparentBlockRemoveButton_Click(object? sender, RoutedEventArgs e)
        {
            _transparentSearchableBlocks.Remove(SelectedBlocks(TransparentBlockView), BlockType.SemiTransparent);
        }
        private static List<BlockEntry<BlockGrouping>> SelectedBlocks(ListBox list)
        {
            return list.SelectedItems?.Cast<BlockEntry<BlockGrouping>>().ToList() ?? new List<BlockEntry<BlockGrouping>>();
        }

        private void ClearSolidBlocksButton_Click(object? sender, RoutedEventArgs e)
        {
            _solidSearchableBlocks.ClearEverything(BlockType.StopAtEncounter);
        }
        private void ClearTransparntBlocksButton_Click(object? sender, RoutedEventArgs e)
        {
            _transparentSearchableBlocks.ClearEverything(BlockType.SemiTransparent);
        }

        private void SolidBlocksResetToDefault_Click(object? sender, RoutedEventArgs e)
        {
            _solidSearchableBlocks.ResetToDefault(Seperate(ScanProfile.DefaultAsset ?? TokenizedBlockAsset<BlockGrouping>.Empty, BlockType.StopAtEncounter).Values.ToList());
        }
        private void TransparentBlocksResetToDefault_Click(object? sender, RoutedEventArgs e)
        {
            _transparentSearchableBlocks.ResetToDefault(Seperate(ScanProfile.DefaultAsset ?? TokenizedBlockAsset<BlockGrouping>.Empty, BlockType.SemiTransparent).Values.ToList());
        }

        private void SolidBlockAddButton_Click(object? sender, RoutedEventArgs e)
        {
            AddBlock(SolidBlockAddButton, SolidBlockView, _solidSearchableBlocks, BlockType.StopAtEncounter);
        }
        private void TransparentBlockAddButton_Click(object? sender, RoutedEventArgs e)
        {
            AddBlock(TransparentBlockAddButton, TransparentBlockView, _transparentSearchableBlocks, BlockType.SemiTransparent);
        }
        private static void AddBlock(Control button, ListBox list, SearchableBlocks container, BlockType type)
        {
            AddBlock addBlock = new("minecraft:");
            Flyout flyout = new() { Content = addBlock, Placement = PlacementMode.Bottom };

            addBlock.Finished += (s, e) => flyout.Hide();
            flyout.Closed += (s, e) =>
            {
                if (addBlock.DialogClosed) return;

                BlockEntry<BlockGrouping> entry = new(addBlock.NameResult) 
                {
                    DefaultValue = new BlockGrouping(type)
                };
                container.Add(entry);

                list.SelectedItem = entry;
                Dispatcher.UIThread.Post(() => list.ScrollIntoView(entry));
            };

            flyout.ShowAt(button);
        }

        private void SetHeightmapAvailability()
        {
            HeightmapComboBox.IsEnabled = HeightmapRadioButton.IsChecked ?? false;
            SetYTextBox.IsEnabled = SetYRadioButton.IsChecked ?? false;
        }

        private void RenderButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            ScanProfile.HeightmapProfile = CreateHeightmapProfile();
            ScanProfile.Asset = CreateAsset();

            DialogClosed = false;
            BlockFilterUpdated?.Invoke(this, EventArgs.Empty);
            if(CloseWindowCheckBox.IsChecked ?? true) Close();
        }

        private HeightmapSettings CreateHeightmapProfile()
        {
            return new HeightmapSettings()
            {
                HeightmapType = (HeightmapRadioButton.IsChecked ?? false) ? HeightmapType.NbtHeightmap : HeightmapType.SetY,
                SetY = short.Parse(SetYTextBox.Text ?? string.Empty),
                NbtHeightmap = HeightmapComboBox.SelectedItem as string ?? string.Empty
            };
        }
        private ITokenizedBlockAsset<BlockGrouping> CreateAsset()
        {
            ITokenizedBlockAsset<BlockGrouping> output = ScanProfile.Asset ?? TokenizedBlockAsset<BlockGrouping>.Empty;
            output.Blocks.Clear();

            foreach (BlockEntry<BlockGrouping> entry in _solidSearchableBlocks.OriginalBlockList)
            {
                if (output.Blocks.ContainsKey(entry.BlockName)) continue;
                output.Blocks.Add(entry.BlockName, entry);
            }

            foreach (BlockEntry<BlockGrouping> entry in _transparentSearchableBlocks.OriginalBlockList)
            {
                if (output.Blocks.ContainsKey(entry.BlockName)) continue;
                output.Blocks.Add(entry.BlockName, entry);
            }

            return output;
        }

        private bool ValidateInput() 
        {
            if (!InputValidator.ValidateInt16(SetYTextBox.Text ?? string.Empty)) 
            {
                return InvalidInput.ShowMessage("Set Y must be a number.");
            }

            return true;
        }
    }
}
