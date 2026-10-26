using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Mapper.Gui
{
    // The find window. Blocks: what to look for on top, what was found below. Signs and Pets: the dimension's signs
    // and named mobs, each with a tick for showing it on the map. Biomes: the dimension's biomes, nearest first, those
    // named shaded on the map. FindPanel, SignsPanel, PetsPanel and BiomesPanel hold what they show, FindTool runs the
    // search, WorldSigns, WorldPets and WorldBiomes read the world; this only lays them out (the Avalonia GUI has its
    // own copy)
    public partial class FindWindow : Window
    {
        public FindPanel Panel { get; }
        public FindTool Tool => Panel.Tool;
        public SignsPanel? Signs { get; }
        public PetsPanel? Pets { get; }
        public BiomesPanel? Biomes { get; }

        private IReadOnlyList<FoundCluster>? _shown;
        private bool _refreshing;
        private string? _message;
        private readonly DispatcherTimer _pinned = new() { Interval = TimeSpan.FromSeconds(2) };
        private IReadOnlyList<SignRow>? _signRows;
        private IReadOnlyList<PetRow>? _petRows;
        private IReadOnlyList<BiomeRow>? _biomeRows;

        public FindWindow(FindTool tool)
        {
            InitializeComponent();

            Panel = new FindPanel(tool);
            BlockList.ItemsSource = Panel.Blocks;
            GroupButtons.ItemsSource = Panel.Groups;
            ChipList.ItemsSource = Panel.Chips;

            _pinned.Tick += (sender, e) =>
            {
                _pinned.Stop();
                PinnedText.Text = string.Empty;
                SignPinnedText.Text = string.Empty;
                PetPinnedText.Text = string.Empty;
                BiomePinnedText.Text = string.Empty;
            };

            Tool.Changed += Tool_Changed;
            Closed += (sender, e) => Tool.Changed -= Tool_Changed;

            if (tool.Signs is WorldSigns signs)
            {
                Signs = new SignsPanel(signs, tool.Canvas);
                signs.Changed += RefreshSigns;
                Closed += (sender, e) =>
                {
                    signs.Changed -= RefreshSigns;
                    signs.Listed = false;
                };
            }
            else
            {
                SignsButton.Visibility = Visibility.Collapsed;
            }

            if (tool.Pets is WorldPets pets)
            {
                Pets = new PetsPanel(pets, tool.Canvas);
                pets.Changed += RefreshPets;
                Closed += (sender, e) =>
                {
                    pets.Changed -= RefreshPets;
                    pets.Listed = false;
                };
            }
            else
            {
                PetsButton.Visibility = Visibility.Collapsed;
            }

            if (tool.Biomes is WorldBiomes biomes)
            {
                Biomes = new BiomesPanel(biomes, tool.Canvas);
                biomes.Changed += RefreshBiomes;
                Closed += (sender, e) =>
                {
                    biomes.Changed -= RefreshBiomes;
                    biomes.Listed = false;
                };
            }
            else
            {
                BiomesButton.Visibility = Visibility.Collapsed;
            }

            Refresh();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = new CustomFrameWindowInitializer(this, Template);
            Half(Tool.Half switch
            {
                FindHalf.Signs when Signs is not null => FindHalf.Signs,
                FindHalf.Pets when Pets is not null => FindHalf.Pets,
                FindHalf.Biomes when Biomes is not null => FindHalf.Biomes,
                _ => FindHalf.Blocks
            });
        }

        // Blocks, signs, pets or biomes
        private void Half(FindHalf half)
        {
            Tool.Half = half;
            bool signs = half == FindHalf.Signs, pets = half == FindHalf.Pets, biomes = half == FindHalf.Biomes;
            BlocksHalf.Visibility = half == FindHalf.Blocks ? Visibility.Visible : Visibility.Collapsed;
            SignsHalf.Visibility = signs ? Visibility.Visible : Visibility.Collapsed;
            PetsHalf.Visibility = pets ? Visibility.Visible : Visibility.Collapsed;
            BiomesHalf.Visibility = biomes ? Visibility.Visible : Visibility.Collapsed;
            BlocksButton.Tag = half == FindHalf.Blocks ? "on" : null;
            SignsButton.Tag = signs ? "on" : null;
            PetsButton.Tag = pets ? "on" : null;
            BiomesButton.Tag = biomes ? "on" : null;
            if (Tool.Signs is WorldSigns worldSigns)
            {
                worldSigns.Listed = signs;
                if (signs) worldSigns.Want();
            }
            if (Tool.Pets is WorldPets worldPets)
            {
                worldPets.Listed = pets;
                if (pets) worldPets.Want();
            }
            if (Tool.Biomes is WorldBiomes worldBiomes)
            {
                worldBiomes.Listed = biomes;
                if (biomes) worldBiomes.Want();
            }
            if (signs)
            {
                RefreshSigns();
                SignSearchBox.Focus();
            }
            else if (pets)
            {
                RefreshPets();
                PetSearchBox.Focus();
            }
            else if (biomes)
            {
                RefreshBiomes();
                BiomeSearchBox.Focus();
            }
            else
            {
                SearchBox.Focus();
            }
        }

        private void BlocksButton_Click(object sender, RoutedEventArgs e) => Half(FindHalf.Blocks);
        private void SignsButton_Click(object sender, RoutedEventArgs e) => Half(FindHalf.Signs);
        private void PetsButton_Click(object sender, RoutedEventArgs e) => Half(FindHalf.Pets);
        private void BiomesButton_Click(object sender, RoutedEventArgs e) => Half(FindHalf.Biomes);

        private void RefreshBiomes()
        {
            if (Biomes is null || BiomesHalf.Visibility != Visibility.Visible) return;
            _refreshing = true;
            try
            {
                IReadOnlyList<BiomeRow> rows = Biomes.Rows();
                if (!ReferenceEquals(rows, _biomeRows))
                {
                    _biomeRows = rows;
                    BiomeList.ItemsSource = rows;
                    BiomePatch? picked = Biomes.Biomes.Picked;
                    if (picked is not null) BiomeList.SelectedItem = rows.FirstOrDefault(r => r.Patch == picked);
                }
                BiomeStatusText.Text = Biomes.Status();
                BiomeProgress.Visibility = Biomes.Biomes.Reading ? Visibility.Visible : Visibility.Hidden;
                BiomeSortedText.Text = Biomes.SortedText();
                BiomeSortButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                bool chosen = BiomeList.SelectedItem is BiomeRow;
                BiomeGoButton.IsEnabled = chosen;
                BiomePinButton.IsEnabled = chosen && Tool.CanPin;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void BiomeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Biomes is null) return;
            Biomes.Filter(BiomeSearchBox.Text);
            RefreshBiomes();
        }

        private void BiomeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || Biomes is null) return;
            BiomeRow? row = BiomeList.SelectedItem as BiomeRow;
            Biomes.Pick(row);
            if (row is not null) Tool.GoTo(row.X, row.Z);
            RefreshBiomes();
        }

        private void BiomeList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => BiomeGoButton_Click(sender, e);

        private void BiomeSortButton_Click(object sender, RoutedEventArgs e)
        {
            Biomes?.SortFromHere();
            RefreshBiomes();
        }

        private void BiomeGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (BiomeList.SelectedItem is BiomeRow row) Tool.GoTo(row.X, row.Z);
        }

        private void BiomePinButton_Click(object sender, RoutedEventArgs e)
        {
            if (BiomeList.SelectedItem is not BiomeRow row || Tool.Biomes is null) return;
            Tool.Biomes.Pin(row.Patch);
            BiomePinnedText.Text = "Pinned";
            _pinned.Stop();
            _pinned.Start();
        }

        private void RefreshPets()
        {
            if (Pets is null || PetsHalf.Visibility != Visibility.Visible) return;
            _refreshing = true;
            try
            {
                IReadOnlyList<PetRow> rows = Pets.Rows();
                if (!ReferenceEquals(rows, _petRows))
                {
                    _petRows = rows;
                    PetList.ItemsSource = rows;
                }
                PetStatusText.Text = Pets.Status();
                PetProgress.Visibility = Pets.Pets.Reading ? Visibility.Visible : Visibility.Hidden;
                PetSortedText.Text = Pets.SortedText();
                PetSortButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                bool chosen = PetList.SelectedItem is PetRow;
                ShowListedPetsButton.IsEnabled = rows.Count > 0 && Pets.Pets.CanShow;
                HideAllPetsButton.IsEnabled = Pets.Pets.ShownHere > 0;
                PetGoButton.IsEnabled = chosen;
                PetPinButton.IsEnabled = chosen && Tool.CanPin;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void PetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Pets is null) return;
            Pets.Filter(PetSearchBox.Text);
            RefreshPets();
        }

        private void PetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing) return;
            if (PetList.SelectedItem is PetRow row) Tool.GoTo(row.Pet);
            RefreshPets();
        }

        private void PetList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PetGoButton_Click(sender, e);

        private void PetSortButton_Click(object sender, RoutedEventArgs e)
        {
            Pets?.SortFromHere();
            RefreshPets();
        }

        private void ShowListedPetsButton_Click(object sender, RoutedEventArgs e) => Pets?.ShowListed();

        private void HideAllPetsButton_Click(object sender, RoutedEventArgs e) => Pets?.HideAll();

        private void PetGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (PetList.SelectedItem is PetRow row) Tool.GoTo(row.Pet);
        }

        private void PetPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (PetList.SelectedItem is not PetRow row || Tool.Pets is null) return;
            Tool.Pets.Pin(row.Pet);
            PetPinnedText.Text = "Pinned";
            _pinned.Stop();
            _pinned.Start();
        }

        private void RefreshSigns()
        {
            if (Signs is null || SignsHalf.Visibility != Visibility.Visible) return;
            _refreshing = true;
            try
            {
                IReadOnlyList<SignRow> rows = Signs.Rows();
                if (!ReferenceEquals(rows, _signRows))
                {
                    _signRows = rows;
                    SignList.ItemsSource = rows;
                }
                SignStatusText.Text = Signs.Status();
                SignProgress.Visibility = Signs.Signs.Reading ? Visibility.Visible : Visibility.Hidden;
                SignSortedText.Text = Signs.SortedText();
                SignSortButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                bool chosen = SignList.SelectedItem is SignRow;
                ShowListedButton.IsEnabled = rows.Count > 0 && Signs.Signs.CanShow;
                HideAllButton.IsEnabled = Signs.Signs.ShownHere > 0;
                SignGoButton.IsEnabled = chosen;
                SignPinButton.IsEnabled = chosen && Tool.CanPin;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void SignSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Signs is null) return;
            Signs.Filter(SignSearchBox.Text);
            RefreshSigns();
        }

        private void BlankBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Signs is null) return;
            Signs.IncludeBlank = BlankBox.IsChecked == true;
            RefreshSigns();
        }

        private void SignList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing) return;
            if (SignList.SelectedItem is SignRow row) Tool.GoTo(row.Sign);
            RefreshSigns();
        }

        private void SignList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SignGoButton_Click(sender, e);

        private void SignSortButton_Click(object sender, RoutedEventArgs e)
        {
            Signs?.SortFromHere();
            RefreshSigns();
        }

        private void ShowListedButton_Click(object sender, RoutedEventArgs e) => Signs?.ShowListed();

        private void HideAllButton_Click(object sender, RoutedEventArgs e) => Signs?.HideAll();

        private void SignGoButton_Click(object sender, RoutedEventArgs e)
        {
            if (SignList.SelectedItem is SignRow row) Tool.GoTo(row.Sign);
        }

        private void SignPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (SignList.SelectedItem is not SignRow row || Tool.Signs is null) return;
            Tool.Signs.Pin(row.Sign);
            SignPinnedText.Text = "Pinned";
            _pinned.Stop();
            _pinned.Start();
        }

        private void Tool_Changed(object? sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            _refreshing = true;
            try
            {
                bool world = Tool.Scene.Domain.CurrentWorld is not null;
                StatusText.Text = _message ?? Panel.Status();
                CountsText.Text = Panel.Counts();
                CountsText.Visibility = CountsText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                SortedText.Text = Panel.SortedText();
                SortButton.Visibility = Tool.Clusters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                Progress.Visibility = Tool.Searching ? Visibility.Visible : Visibility.Hidden;
                Progress.Value = Tool.RegionsTotal == 0 ? 0 : (double)Tool.RegionsDone / Tool.RegionsTotal;

                FindButton.IsEnabled = world;
                StopButton.IsEnabled = Tool.Searching;

                if (!ReferenceEquals(_shown, Tool.Clusters))
                {
                    _shown = Tool.Clusters;
                    FoundCluster? picked = Tool.Picked;
                    IReadOnlyList<FindResultRow> rows = Panel.Rows();
                    ResultList.ItemsSource = rows;
                    if (picked is not null) ResultList.SelectedItem = rows.FirstOrDefault(r => r.Cluster == picked);
                }

                bool chosen = ResultList.SelectedItem is FindResultRow;
                GoButton.IsEnabled = chosen;
                PinButton.IsEnabled = chosen && Tool.CanPin;
            }
            finally
            {
                _refreshing = false;
            }
        }

        // A message in place of the status line, until the next search or change
        private void Say(string? message)
        {
            _message = message;
            Refresh();
        }

        private void AddBlock(FindBlockRow? block)
        {
            if (block is null) return;
            Say(Panel.Add(block));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Panel?.Filter(SearchBox.Text);
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddBlock(BlockList.SelectedItem as FindBlockRow ?? Panel.Blocks.FirstOrDefault());
                e.Handled = true;
            }
            else if (e.Key == Key.Down && Panel.Blocks.Count > 0)
            {
                BlockList.SelectedIndex = Math.Max(0, BlockList.SelectedIndex);
                (BlockList.ItemContainerGenerator.ContainerFromIndex(BlockList.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        }

        private void BlockList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AddBlock(BlockList.SelectedItem as FindBlockRow);

        private void BlockList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddBlock(BlockList.SelectedItem as FindBlockRow);
            e.Handled = true;
        }

        private void GroupButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FindTarget group) Say(Panel.Add(group));
        }

        private void RemoveChip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FindChip chip) Panel.Remove(chip);
            Say(null);
        }

        private void HeightBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            FindButton_Click(sender, e);
            e.Handled = true;
        }

        private void FindButton_Click(object sender, RoutedEventArgs e)
        {
            BlockQuery? query = Panel.Query(FromBox.Text, ToBox.Text, out string? problem);
            _message = problem;
            if (query is null)
            {
                Refresh();
                return;
            }
            Tool.Start(query);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) => Tool.Stop();

        private void SortButton_Click(object sender, RoutedEventArgs e) => Tool.SortFromHere();

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing) return;
            Tool.GoTo((ResultList.SelectedItem as FindResultRow)?.Cluster);
        }

        private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => GoButton_Click(sender, e);

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList.SelectedItem is FindResultRow row) Tool.GoTo(row.Cluster);
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList.SelectedItem is not FindResultRow row) return;
            Tool.Pin(row.Cluster);
            PinnedText.Text = "Pinned";
            _pinned.Stop();
            _pinned.Start();
        }
    }
}
