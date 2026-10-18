using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Mapper.Gui
{
    public partial class WorldPanel : UserControl
    {
        public WorldPanelEntry Entry { get; }

        private bool _isDown = false;
        private readonly IBrush _redBrush;
        private readonly IBrush? _background;
        private readonly IBrush _mouseOver;
        private readonly IBrush _mouseDown;

        public WorldPanel(WorldPanelEntry entry)
        {
            InitializeComponent();

            _redBrush = new SolidColorBrush(Color.FromRgb(233, 0, 0));
            _background = Background;
            _mouseOver = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            _mouseDown = new SolidColorBrush(Color.FromRgb(50, 50, 50));

            Entry = entry;

            SetContent();
        }

        private void SetContent()
        {
            WorldIcon.Source = Entry.Icon;
            IngameNameLabel.Text = Entry.Level.WorldName;

            DateTime lastPlayed = Entry.Level.LastPlayed;
            DirectoryNameLabel.Text = $"{Path.GetFileName(Entry.Source.TrimEnd(Path.DirectorySeparatorChar))} ({lastPlayed:dd.MM.yyyy HH:mm})";

            if (!Entry.Level.IsHardcode)
            {
                GameTypeLabel.Text = $"{Entry.Level.GameType} Mode";
            }
            else
            {
                GameTypeLabel.Text = "Hardcore Mode";
                GameTypeLabel.Foreground = _redBrush;
                GameTypeLabel.FontWeight = FontWeight.SemiBold;
            }

            if (Entry.Level.AllowCommands)
            {
                CheatsLabel.Text = "Cheats";
            }
            else
            {
                CheatsCommaLabel.Text = string.Empty;
            }

            VersionLabel.Text = Entry.Level.Version.VersionName;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_isDown) Background = _mouseOver;
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            Background = _mouseDown;
            _isDown = true;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            Background = _mouseOver;
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            Background = _background;
            _isDown = false;
        }
    }
}
