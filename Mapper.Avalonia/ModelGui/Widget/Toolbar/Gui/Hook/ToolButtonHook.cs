using Mapper.Gui.Model;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Mapper.Gui
{
    // Keeps a toolbar button looking as its tool is: gold with a bar under it while on (or, for Overlays, while any
    // overlay is), lighter under the mouse, greyed while it can't be used
    public class ToolButtonHook
    {
        public static readonly Color Ink = Color.FromRgb(216, 216, 216);
        public static readonly Color OnInk = Color.FromRgb(255, 216, 77);
        private static readonly Color OverInk = Color.FromRgb(255, 255, 255);
        private static readonly Color DisabledInk = Color.FromRgb(90, 90, 90);
        private static readonly Color DisabledOnInk = Color.FromRgb(140, 123, 58);

        public IToolButton ToolButton { get; set; }

        public MouseColorProperties TurnedOff { get; } = new MouseColorProperties()
        {
            Default = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            MouseOver = new SolidColorBrush(Color.FromRgb(46, 46, 46)),
            MouseDown = new SolidColorBrush(Color.FromRgb(58, 58, 58))
        };
        public MouseColorProperties TurnedOn { get; } = new MouseColorProperties()
        {
            Default = new SolidColorBrush(Color.FromRgb(58, 52, 32)),
            MouseOver = new SolidColorBrush(Color.FromRgb(69, 62, 39)),
            MouseDown = new SolidColorBrush(Color.FromRgb(80, 72, 45))
        };

        private enum MouseState
        {
            Default,
            Over,
            Down
        }

        private readonly SolidColorBrush _ink;
        private readonly Border _bar;
        private readonly Border _badge;
        private readonly TextBlock _count;
        private bool _pressed = false;

        public ToolButtonHook(IToolButton tool, SolidColorBrush ink, Border bar, Border badge, TextBlock count)
        {
            ToolButton = tool;
            _ink = ink;
            _bar = bar;
            _badge = badge;
            _count = count;

            StartHook();
            Refresh();
        }

        private bool IsOn => ToolButton.Tool.IsTurnedOn || ToolButton.Badge > 0;

        private void StartHook()
        {
            ToolButton.Tool.OnTurnedOn += isOn => Refresh();
            ToolButton.Tool.OnEnabled += OnEnabled;
            ToolButton.BadgeChanged += Refresh;

            if (ToolButton.Button is null) return;

            ToolButton.Button.IsEnabled = ToolButton.Tool.Enabled;
            ToolButton.Button.Click += (sender, e) => ToolButton.Tool.IsTurnedOn = !ToolButton.Tool.IsTurnedOn;
            ToolButton.Button.AddHandler(InputElement.PointerPressedEvent, (sender, e) =>
            {
                _pressed = true;
                Refresh();
            }, RoutingStrategies.Tunnel, true);
            ToolButton.Button.AddHandler(InputElement.PointerReleasedEvent, (sender, e) =>
            {
                _pressed = false;
                Refresh();
            }, RoutingStrategies.Tunnel, true);
            ToolButton.Button.PointerExited += (sender, e) => Refresh();
            ToolButton.Button.PointerEntered += (sender, e) => Refresh();
        }

        private void OnEnabled(bool isEnabled)
        {
            if (ToolButton.Button is not null) ToolButton.Button.IsEnabled = isEnabled;
            Refresh();
        }

        private MouseState GetCurrentMouseState()
        {
            if (ToolButton.Button is null || !ToolButton.Button.IsPointerOver || !ToolButton.Tool.Enabled) return MouseState.Default;
            return _pressed ? MouseState.Down : MouseState.Over;
        }

        private void Refresh()
        {
            bool on = IsOn, enabled = ToolButton.Tool.Enabled;
            MouseState mouse = GetCurrentMouseState();

            _ink.Color = !enabled ? (on ? DisabledOnInk : DisabledInk) : on ? OnInk : mouse == MouseState.Default ? Ink : OverInk;
            _bar.IsVisible = on;
            _bar.Opacity = enabled ? 1 : 0.5;
            _badge.IsVisible = ToolButton.Badge > 0;
            _count.Text = ToolButton.Badge.ToString();

            if (ToolButton.Button is null) return;

            MouseColorProperties properties = on && enabled ? TurnedOn : TurnedOff;
            ToolButton.Button.Background = mouse switch
            {
                MouseState.Default => properties.Default,
                MouseState.Over => properties.MouseOver,
                _ => properties.MouseDown,
            };
        }
    }
}
