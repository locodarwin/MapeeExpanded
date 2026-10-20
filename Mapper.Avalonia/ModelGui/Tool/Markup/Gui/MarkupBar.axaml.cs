using Mapper.Gui.Controller;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Mapper.Gui
{
    // Under the toolbar while the Markup button is on: what a click on the map does, the Places list, undo, and
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

        public MarkupBar(MarkupTool tool, CanvasControl canvas)
        {
            InitializeComponent();

            Tool = tool;
            Canvas = canvas;

            IsVisible = tool.IsTurnedOn;
            tool.OnTurnedOn += isTurnedOn =>
            {
                IsVisible = isTurnedOn;
                if (!isTurnedOn) PlacesPopup.IsOpen = false;
            };

            tool.Editor.ModeChanged += (sender, e) => ShowMode();
            tool.Editor.Changed += (sender, e) => ShowHistory();
            tool.Editor.EditRequested += (sender, request) => OpenEditor(request);
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
            PlacesList.DoubleTapped += (sender, e) =>
            {
                if (PlacesList.SelectedItem is not ListBoxItem { Tag: MarkupItem item }) return;
                PlacesPopup.IsOpen = false;
                Tool.Editor.Mode = MarkupMode.Select;
                Tool.Editor.Select(item);
                Dispatcher.UIThread.Post(() =>
                {
                    if (Tool.Editor.Find(item.Id) is MarkupItem found) OpenEditor(new MarkupEditRequest(found.CopyItem(), false, found));
                });
            };

            ShowMode();
            ShowHistory();
        }

        private void ShowMode()
        {
            MarkupMode mode = Tool.Editor.Mode;
            PinButton.Classes.Set("selected", mode == MarkupMode.Pin);
            ZoneButton.Classes.Set("selected", mode == MarkupMode.Zone);
            RectangleButton.Classes.Set("selected", mode == MarkupMode.Rectangle);
            SelectButton.Classes.Set("selected", mode == MarkupMode.Select);

            Canvas.Cursor = mode is MarkupMode.Pin or MarkupMode.Zone or MarkupMode.Rectangle ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
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
            });
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
            EditPopup.PlacementRect = new Rect(Math.Clamp(x, 0, Canvas.ActualWidth), Math.Clamp(y + below, 0, Canvas.ActualHeight), 1, 1);
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
            string filter = (PlacesFilter.Text ?? string.Empty).Trim();
            var places = Tool.Places().Where(p => filter.Length == 0 || MarkupTool.Title(p).Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                (p.Note?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();

            PlacesList.Items.Clear();
            foreach (MarkupItem item in places)
            {
                StackPanel content = new();
                content.Children.Add(new TextBlock() { Text = (item is MapPin ? "● " : "▢ ") + MarkupTool.Title(item), FontSize = 12, Foreground = Avalonia.Media.Brushes.White });
                content.Children.Add(new TextBlock() { Text = MarkupTool.Describe(item), FontSize = 11, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(170, 170, 170)) });
                PlacesList.Items.Add(new ListBoxItem() { Content = content, Tag = item });
            }

            PlacesEmpty.IsVisible = places.Count == 0;
            PlacesEmpty.Text = Tool.Places().Count == 0 ? "Nothing marked in this dimension yet." : "Nothing matches.";
            PlacesList.IsVisible = places.Count > 0;
        }

        private void PinButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Pin;
        private void ZoneButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Zone;
        private void RectangleButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Rectangle;
        private void SelectButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Mode = MarkupMode.Select;
        private void UndoButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Undo();
        private void RedoButton_Click(object? sender, RoutedEventArgs e) => Tool.Editor.Redo();
        private void HideButton_Click(object? sender, RoutedEventArgs e) => Tool.ShowMarkup = false;

        private void PlacesButton_Click(object? sender, RoutedEventArgs e)
        {
            PlacesFilter.Text = string.Empty;
            FillPlaces();
            PlacesPopup.IsOpen = !PlacesPopup.IsOpen;
            if (PlacesPopup.IsOpen) PlacesFilter.Focus();
        }

        private async void ExportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel top) return;
            if (Tool.Editor.Document.IsEmpty)
            {
                ShowMessage("This world has no markup to export yet.");
                return;
            }

            string name = Path.GetFileNameWithoutExtension(Tool.Editor.Document.World?.TrimEnd('/', '\\') ?? "world");
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                Title = "Export markup",
                SuggestedFileName = $"{Tool.Editor.Document.Name ?? name} markup",
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("Markup files (.json)") { Patterns = ["*.json"] }]
            });
            if (file?.TryGetLocalPath() is not string path) return;

            try
            {
                Tool.Export(path);
                ShowMessage($"Saved {Path.GetFileName(path)}.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ShowMessage($"Couldn't save the markup: {exception.Message}");
            }
        }

        private async void ImportButton_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel top) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Import markup",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Markup files (.json)") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not string path) return;

            ShowMessage(Tool.Import(path));
        }
    }
}
