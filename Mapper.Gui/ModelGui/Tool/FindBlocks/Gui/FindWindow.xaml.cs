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
    // The find window: what to look for on top, what was found below. FindPanel holds what it shows, FindTool runs
    // the search; this only lays them out (the Avalonia GUI has its own copy)
    public partial class FindWindow : Window
    {
        public FindPanel Panel { get; }
        public FindTool Tool => Panel.Tool;

        private IReadOnlyList<FoundCluster>? _shown;
        private bool _refreshing;
        private string? _message;
        private readonly DispatcherTimer _pinned = new() { Interval = TimeSpan.FromSeconds(2) };

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
            };

            Tool.Changed += Tool_Changed;
            Closed += (sender, e) => Tool.Changed -= Tool_Changed;

            Refresh();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = new CustomFrameWindowInitializer(this, Template);
            SearchBox.Focus();
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
