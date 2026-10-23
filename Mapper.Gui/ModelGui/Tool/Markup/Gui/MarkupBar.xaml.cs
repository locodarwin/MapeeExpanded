using Mapper.Gui.Controller;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mapper.Gui
{
    // Under the toolbar while the Mark button is on: what a click on the map does, the Places list, undo, and
    // markup files. It also shows the details popup whenever the editor asks for one
    public partial class MarkupBar : UserControl
    {
        public MarkupTool Tool { get; }
        public CanvasControl Canvas { get; }

        // The details popup while it's open
        public MarkupItemEditor? ItemEditor => _itemEditor;
        private MarkupItemEditor? _itemEditor;
        private MarkupEditRequest? _request;
        private bool _finished;
        private DispatcherTimer? _messageTimer;

        private static readonly Brush DetailBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));

        public MarkupBar(MarkupTool tool, CanvasControl canvas, ShortcutKeys keys)
        {
            InitializeComponent();

            Tool = tool;
            Canvas = canvas;

            Visibility = tool.IsTurnedOn ? Visibility.Visible : Visibility.Collapsed;
            tool.OnTurnedOn += isTurnedOn =>
            {
                Visibility = isTurnedOn ? Visibility.Visible : Visibility.Collapsed;
                if (!isTurnedOn) PlacesPopup.IsOpen = false;
            };

            tool.Editor.ModeChanged += (sender, e) => ShowMode();
            tool.Editor.Changed += (sender, e) => ShowHistory();
            tool.Editor.EditRequested += (sender, request) => Dispatcher.BeginInvoke(() => OpenEditor(request));
            tool.PlacesChanged += (sender, e) =>
            {
                if (PlacesPopup.IsOpen) FillPlaces();
            };

            EditPopup.PlacementTarget = canvas;
            EditPopup.Closed += (sender, e) => EditorClosed();

            PlacesPopup.PlacementTarget = PlacesButton;
            PlacesFilter.TextChanged += (sender, e) => FillPlaces();
            PlacesList.SelectionChanged += (sender, e) =>
            {
                if (PlacesList.SelectedItem is ListBoxItem { Tag: MarkupItem item })
                {
                    Tool.GoTo(item);
                    Tool.Editor.Select(item);
                }
            };
            PlacesList.MouseDoubleClick += (sender, e) =>
            {
                if (PlacesList.SelectedItem is not ListBoxItem { Tag: MarkupItem item }) return;
                PlacesPopup.IsOpen = false;
                Tool.Editor.Mode = MarkupMode.Select;
                Tool.Editor.Select(item);
                Dispatcher.BeginInvoke(() =>
                {
                    if (Tool.Editor.Find(item.Id) is MarkupItem found) OpenEditor(new MarkupEditRequest(found.CopyItem(), false, found));
                });
            };

            ShowMode();
            ShowHistory();
            ShowKeys(keys.Map);
            keys.Changed += () => ShowKeys(keys.Map);
        }

        // The mode buttons' tooltips name their keys, as they are now
        private void ShowKeys(KeyMap keys)
        {
            void Tip(Button button, string text, ShortcutAction action) => button.ToolTip = keys.WithKey(text, action);

            Tip(PinButton, "Click the map to drop a pin", ShortcutAction.PinMode);
            Tip(ZoneButton, "Click the corners of a zone", ShortcutAction.ZoneMode);
            Tip(RectangleButton, "Drag out a rectangular zone", ShortcutAction.RectangleMode);
            Tip(SelectButton, "Pick, move and edit pins and zones", ShortcutAction.SelectMode);
            string show = keys.KeyText(ShortcutAction.ShowMarkup) is string key ? $" or {key}" : "";
            HideButton.ToolTip = $"Hide pins and zones until the Mark button{show} brings them back";
        }

        private void ShowMode()
        {
            MarkupMode mode = Tool.Editor.Mode;
            PinButton.Tag = mode == MarkupMode.Pin ? "selected" : null;
            ZoneButton.Tag = mode == MarkupMode.Zone ? "selected" : null;
            RectangleButton.Tag = mode == MarkupMode.Rectangle ? "selected" : null;
            SelectButton.Tag = mode == MarkupMode.Select ? "selected" : null;

            Canvas.Cursor = mode is MarkupMode.Pin or MarkupMode.Zone or MarkupMode.Rectangle ? Cursors.Cross : null;
            if (_messageTimer is null) HintText.Text = Hint(mode);
        }

        private static string Hint(MarkupMode mode) => mode switch
        {
            MarkupMode.Pin => "Click the map to drop a pin, or click a pin to change it. Drag to move the map as usual.",
            MarkupMode.Zone => "Click each corner in turn. Double-click, press Enter or click the first corner to finish; Backspace takes the last corner back. Hold Shift to snap to chunk corners.",
            MarkupMode.Rectangle => "Drag across the map to mark out a rectangle. Hold Shift to take in whole chunks.",
            MarkupMode.Select => "Click a pin or zone to pick it, double-click to change it, Delete to remove it. Drag a pin, or a picked zone's corners, to move them; double-click a picked zone's edge to add a corner, or a corner to remove it.",
            _ => string.Empty
        };

        private void ShowMessage(string message)
        {
            _messageTimer?.Stop();
            HintText.Text = message;
            _messageTimer = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (sender, e) =>
            {
                _messageTimer?.Stop();
                _messageTimer = null;
                HintText.Text = Hint(Tool.Editor.Mode);
            }, Dispatcher);
            _messageTimer.Start();
        }

        private void ShowHistory()
        {
            UndoButton.IsEnabled = Tool.Editor.CanUndo;
            RedoButton.IsEnabled = Tool.Editor.CanRedo;
        }

        private void OpenEditor(MarkupEditRequest request)
        {
            if (EditPopup.IsOpen) EditPopup.IsOpen = false;

            _request = request;
            _finished = false;
            _itemEditor = new MarkupItemEditor(request.Item, request.IsNew);
            _itemEditor.Done += (sender, result) => Finish(result);

            var (x, y) = Tool.AnchorOf(request.Original);
            double below = request.Item is MapPin ? MarkupEditor.PinRadius + 6 : 14;
            EditPopup.HorizontalOffset = Math.Clamp(x, 0, Canvas.ActualWidth) - _itemEditor.Width / 2;
            EditPopup.VerticalOffset = Math.Clamp(y + below, 0, Canvas.ActualHeight);
            EditPopup.Child = _itemEditor;
            EditPopup.IsOpen = true;
        }

        private void Finish(MarkupEditResult result)
        {
            if (_request is not MarkupEditRequest request || _finished) return;
            _finished = true;

            switch (result)
            {
                case MarkupEditResult.Save:
                    Tool.Editor.Commit(request.Item);
                    break;
                case MarkupEditResult.Cancel:
                    Tool.Editor.Cancel(request.Item);
                    break;
                case MarkupEditResult.Delete:
                    Tool.Editor.Delete(request.Original);
                    break;
            }
            EditPopup.IsOpen = false;
            Canvas.Focus();
        }

        // A click away from the popup keeps what was typed, so a pin dropped and named isn't lost
        private void EditorClosed()
        {
            if (!_finished && _itemEditor is not null)
            {
                _itemEditor.ReadFields();
                Finish(MarkupEditResult.Save);
            }
            _request = null;
            _itemEditor = null;
            EditPopup.Child = null;
        }

        private void FillPlaces()
        {
            string filter = PlacesFilter.Text.Trim();
            var all = Tool.Places();
            var places = all.Where(p => filter.Length == 0 || MarkupTool.Title(p).Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                (p.Note?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();

            PlacesList.Items.Clear();
            foreach (MarkupItem item in places)
            {
                StackPanel content = new();
                content.Children.Add(new TextBlock() { Text = (item is MapPin ? "● " : "▢ ") + MarkupTool.Title(item), FontSize = 12, Foreground = Brushes.White });
                content.Children.Add(new TextBlock() { Text = MarkupTool.Describe(item), FontSize = 11, Foreground = DetailBrush });
                PlacesList.Items.Add(new ListBoxItem() { Content = content, Tag = item, Padding = new Thickness(8, 3, 8, 3) });
            }

            PlacesEmpty.Visibility = places.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PlacesEmpty.Text = all.Count == 0 ? "Nothing marked in this dimension yet." : "Nothing matches.";
            PlacesList.Visibility = places.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Pin;
        private void ZoneButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Zone;
        private void RectangleButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Rectangle;
        private void SelectButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Select;
        private void UndoButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Undo();
        private void RedoButton_Click(object sender, RoutedEventArgs e) => Tool.Editor.Redo();
        private void HideButton_Click(object sender, RoutedEventArgs e) => Tool.ShowMarkup = false;

        private void PlacesButton_Click(object sender, RoutedEventArgs e)
        {
            PlacesFilter.Text = string.Empty;
            FillPlaces();
            PlacesPopup.IsOpen = !PlacesPopup.IsOpen;
            if (PlacesPopup.IsOpen) Dispatcher.BeginInvoke(() => Keyboard.Focus(PlacesFilter), DispatcherPriority.Input);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tool.Editor.Document.IsEmpty)
            {
                ShowMessage("This world has no markup to export yet.");
                return;
            }

            string name = Path.GetFileNameWithoutExtension(Tool.Editor.Document.World?.TrimEnd('/', '\\') ?? "world");
            Microsoft.Win32.SaveFileDialog dialog = new()
            {
                Title = "Export markup",
                FileName = $"{Tool.Editor.Document.Name ?? name} markup",
                DefaultExt = ".json",
                Filter = "Markup files (.json)|*.json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                Tool.Export(dialog.FileName);
                ShowMessage($"Saved {Path.GetFileName(dialog.FileName)}.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ShowMessage($"Couldn't save the markup: {exception.Message}");
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                Title = "Import markup",
                Filter = "Markup files (.json)|*.json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            ShowMessage(Tool.Import(dialog.FileName));
        }
    }
}
