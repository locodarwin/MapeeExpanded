using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Mapper.Gui
{
    // The find window. Blocks: what to look for on top, what was found below. Signs: the dimension's signs, each with
    // a tick for showing it on the map. FindPanel and SignsPanel hold what they show, FindTool runs the search and
    // WorldSigns reads the signs; this only lays them out (the WPF GUI has its own copy)
    public partial class FindWindow : Window
    {
        public FindPanel Panel { get; }
        public FindTool Tool => Panel.Tool;
        public SignsPanel? Signs { get; }

        private IReadOnlyList<FoundCluster>? _shown;
        private bool _refreshing;
        private string? _message;
        private IReadOnlyList<SignRow>? _signRows;

        public FindWindow(FindTool tool)
        {
            InitializeComponent();

            Panel = new FindPanel(tool);
            BlockList.ItemsSource = Panel.Blocks;
            GroupButtons.ItemsSource = Panel.Groups;
            ChipList.ItemsSource = Panel.Chips;

            SearchBox.TextChanged += (sender, e) => Panel.Filter(SearchBox.Text ?? string.Empty);
            // Ahead of the box, which would take the arrow keys for its caret
            SearchBox.AddHandler(KeyDownEvent, (object? sender, KeyEventArgs e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AddBlock(BlockList.SelectedItem as FindBlockRow ?? Panel.Blocks.FirstOrDefault());
                    e.Handled = true;
                }
                else if (e.Key == Key.Down && Panel.Blocks.Count > 0)
                {
                    BlockList.SelectedIndex = Math.Max(0, BlockList.SelectedIndex);
                    BlockList.ContainerFromIndex(BlockList.SelectedIndex)?.Focus();
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            BlockList.DoubleTapped += (sender, e) => AddBlock(BlockList.SelectedItem as FindBlockRow);
            BlockList.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Enter) return;
                AddBlock(BlockList.SelectedItem as FindBlockRow);
                e.Handled = true;
            };

            FromBox.KeyDown += FindOnEnter;
            ToBox.KeyDown += FindOnEnter;

            ResultList.SelectionChanged += (sender, e) =>
            {
                if (_refreshing) return;
                Tool.GoTo((ResultList.SelectedItem as FindResultRow)?.Cluster);
            };
            ResultList.DoubleTapped += (sender, e) => GoButton_Click(sender, e);
            SignList.DoubleTapped += (sender, e) => SignGoButton_Click(sender, e);

            Tool.Changed += Tool_Changed;
            Closed += (sender, e) => Tool.Changed -= Tool_Changed;

            if (tool.Signs is WorldSigns signs)
            {
                Signs = new SignsPanel(signs, tool.Canvas);
                SignSearchBox.TextChanged += (sender, e) =>
                {
                    Signs.Filter(SignSearchBox.Text ?? string.Empty);
                    RefreshSigns();
                };
                BlankBox.IsCheckedChanged += (sender, e) =>
                {
                    Signs.IncludeBlank = BlankBox.IsChecked == true;
                    RefreshSigns();
                };
                SignList.SelectionChanged += (sender, e) =>
                {
                    if (_refreshing) return;
                    if (SignList.SelectedItem is SignRow row) Tool.GoTo(row.Sign);
                    RefreshSigns();
                };
                signs.Changed += RefreshSigns;
                Closed += (sender, e) =>
                {
                    signs.Changed -= RefreshSigns;
                    signs.Listed = false;
                };
            }
            else
            {
                SignsButton.IsVisible = false;
            }

            Opened += (sender, e) => Half(Tool.OnSigns && Signs is not null);

            Refresh();
        }

        // Blocks or signs
        private void Half(bool signs)
        {
            Tool.OnSigns = signs;
            BlocksHalf.IsVisible = !signs;
            SignsHalf.IsVisible = signs;
            BlocksButton.Classes.Set("on", !signs);
            SignsButton.Classes.Set("on", signs);
            if (Tool.Signs is WorldSigns worldSigns)
            {
                worldSigns.Listed = signs;
                if (signs) worldSigns.Want();
            }
            if (signs)
            {
                RefreshSigns();
                SignSearchBox.Focus();
            }
            else
            {
                SearchBox.Focus();
            }
        }

        private void BlocksButton_Click(object? sender, RoutedEventArgs e) => Half(false);
        private void SignsButton_Click(object? sender, RoutedEventArgs e) => Half(true);

        private void RefreshSigns()
        {
            if (Signs is null || !SignsHalf.IsVisible) return;
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
                SignProgress.IsVisible = Signs.Signs.Reading;
                SignSortedText.Text = Signs.SortedText();
                SignSortButton.IsVisible = rows.Count > 0;

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

        private void SignSortButton_Click(object? sender, RoutedEventArgs e)
        {
            Signs?.SortFromHere();
            RefreshSigns();
        }

        private void ShowListedButton_Click(object? sender, RoutedEventArgs e) => Signs?.ShowListed();

        private void HideAllButton_Click(object? sender, RoutedEventArgs e) => Signs?.HideAll();

        private void SignGoButton_Click(object? sender, RoutedEventArgs e)
        {
            if (SignList.SelectedItem is SignRow row) Tool.GoTo(row.Sign);
        }

        private void SignPinButton_Click(object? sender, RoutedEventArgs e)
        {
            if (SignList.SelectedItem is not SignRow row || Tool.Signs is null) return;
            Tool.Signs.Pin(row.Sign);
            SignPinnedText.Text = "Pinned";
            DispatcherTimer.RunOnce(() => SignPinnedText.Text = string.Empty, TimeSpan.FromSeconds(2));
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
                CountsText.IsVisible = CountsText.Text.Length > 0;
                SortedText.Text = Panel.SortedText();
                SortButton.IsVisible = Tool.Clusters.Count > 0;

                Progress.IsVisible = Tool.Searching;
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

        private void GroupButton_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FindTarget group) Say(Panel.Add(group));
        }

        private void RemoveChip_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is FindChip chip) Panel.Remove(chip);
            Say(null);
        }

        private void FindOnEnter(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            FindButton_Click(sender, e);
            e.Handled = true;
        }

        private void FindButton_Click(object? sender, RoutedEventArgs e)
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

        private void StopButton_Click(object? sender, RoutedEventArgs e) => Tool.Stop();

        private void SortButton_Click(object? sender, RoutedEventArgs e) => Tool.SortFromHere();

        private void GoButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ResultList.SelectedItem is FindResultRow row) Tool.GoTo(row.Cluster);
        }

        private void PinButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ResultList.SelectedItem is not FindResultRow row) return;
            Tool.Pin(row.Cluster);
            PinnedText.Text = "Pinned";
            DispatcherTimer.RunOnce(() => PinnedText.Text = string.Empty, TimeSpan.FromSeconds(2));
        }
    }
}
