using System;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
#endif

namespace Mapper.Gui
{
    // Over the map while something is dragged onto the window, saying what dropping it would do, and briefly after a
    // drop to say how it went. Never in the way of the mouse
    public class DropOverlay : UserControl
    {
        private readonly TextBlock _text;
        private readonly Border _card;
        private readonly DispatcherTimer _timer = new();

        private static readonly SolidColorBrush Accept = new(Color.FromRgb(255, 215, 0));
        private static readonly SolidColorBrush Reject = new(Color.FromRgb(112, 112, 112));

        public bool IsShowing { get; private set; }
        public string? Text => IsShowing ? _text.Text : null;

        public DropOverlay()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            IsHitTestVisible = false;

            _text = new TextBlock()
            {
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 460
            };
            _card = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(22, 14, 22, 14),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _text
            };
            Content = new Border() { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), Child = _card };

            _timer.Tick += (sender, e) => Hide();
            Hide();
        }

        // While dragging: accept says whether a drop would do anything
        public void ShowHint(string text, bool accept)
        {
            _timer.Stop();
            Show(text, accept);
        }

        // After a drop, for a few seconds
        public void ShowMessage(string text, bool good)
        {
            Show(text, good);
            _timer.Interval = TimeSpan.FromSeconds(4);
            _timer.Start();
        }

        private void Show(string text, bool accept)
        {
            _text.Text = text;
            _card.BorderBrush = accept ? Accept : Reject;
            IsShowing = true;
#if AVALONIA
            IsVisible = true;
#else
            Visibility = Visibility.Visible;
#endif
        }

        public void Hide()
        {
            _timer.Stop();
            IsShowing = false;
#if AVALONIA
            IsVisible = false;
#else
            Visibility = Visibility.Collapsed;
#endif
        }
    }
}
