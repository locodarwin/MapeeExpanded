using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldEditor;
#if AVALONIA
using Avalonia;
using Avalonia.Input;
#else
using System.Windows;
using System.Windows.Input;
#endif

namespace Mapper.Gui.Controller
{
    // Carries out the key map's actions by turning the same tools and widgets the buttons and panels do. Each GUI
    // decides which keys reach it (none while typing) and supplies what only it has: the key list and full screen
    public class ShortcutRunner
    {
        public KeyMap Keys { get; }
        public ToolbarWidget Toolbar { get; }
        public DimensionWidget Dimensions { get; }
        public StylebarWidget Styles { get; }
        public HeightWidget Height { get; }
        public ScaleBehaviour View { get; }
        public CanvasControl Canvas { get; }

        public Action? ShowHelp { get; set; }
        public Action? ToggleFullScreen { get; set; }
        // Leaves full screen and says whether it was on, so Esc does nothing else when it wasn't
        public Func<bool>? LeaveFullScreen { get; set; }
        // True while the keys belong to something else, such as the markup details popup, wherever its focus is
        public Func<bool>? Paused { get; set; }

        // How far the arrow keys move the map, in screen units
        private const double Step = 16;

        public ShortcutRunner(KeyMap keys, ToolbarWidget toolbar, DimensionWidget dimensions, StylebarWidget styles, HeightWidget height, ScaleBehaviour view, CanvasControl canvas)
        {
            Keys = keys;
            Toolbar = toolbar;
            Dimensions = dimensions;
            Styles = styles;
            Height = height;
            View = view;
            Canvas = canvas;
        }

        // Whether the key did something, so the GUI can stop it going further
        public bool Run(Key key, bool control, bool shift)
        {
            if (Paused?.Invoke() == true) return false;
            ShortcutModifiers modifiers = (control ? ShortcutModifiers.Control : 0) | (shift ? ShortcutModifiers.Shift : 0);
            return Keys.Find(KeyOf(key), modifiers) is KeyBinding binding && Run(binding.Action, binding.Argument);
        }

        public bool Run(ShortcutAction action, int argument = 0)
        {
            switch (action)
            {
                case ShortcutAction.Dimension:
                    if (Toolbar.Renderer.Scene.Domain.CurrentWorld is null) return false;
                    List<Dimension> all = [.. Dimensions.Dimensions.Select(d => d.Dimension), .. Dimensions.ExtraDimensions];
                    if (argument >= all.Count) return false;
                    if (Dimensions.CurrentDimension != all[argument]) Dimensions.CurrentDimension = all[argument];
                    return true;

                case ShortcutAction.ZoomIn:
                    View.ZoomIn(Centre);
                    return true;
                case ShortcutAction.ZoomOut:
                    View.ZoomOut(Centre);
                    return true;
                case ShortcutAction.ResetZoom:
                    View.ResetZoom();
                    return true;

                case ShortcutAction.PanUp: return Pan(0, 1, argument);
                case ShortcutAction.PanDown: return Pan(0, -1, argument);
                case ShortcutAction.PanLeft: return Pan(1, 0, argument);
                case ShortcutAction.PanRight: return Pan(-1, 0, argument);

                case ShortcutAction.PreviousStyle: return StepStyle(-1);
                case ShortcutAction.NextStyle: return StepStyle(1);

                case ShortcutAction.HeightMode:
                    if (!Height.Enabled) return false;
                    Height.Mode = Height.Mode switch
                    {
                        HeightMode.Surface => HeightMode.Slice,
                        HeightMode.Slice => HeightMode.Caves,
                        _ => HeightMode.Surface
                    };
                    return true;
                case ShortcutAction.HeightDown:
                case ShortcutAction.HeightUp:
                    if (!Height.Enabled) return false;
                    Height.Y += action == ShortcutAction.HeightUp ? argument : -argument;
                    return true;

                case ShortcutAction.Grid: return Toggle(Toolbar.Overlays["grid"]);
                case ShortcutAction.SlimeChunks: return Toggle(Toolbar.Overlays["slime_chunks"]);
                case ShortcutAction.Axes: return Toggle(Toolbar.Overlays["axes"]);
                case ShortcutAction.ChunkHighlight: return Toggle(Toolbar.Overlays["chunk_highlight"]);
                case ShortcutAction.MeasureLength: return Toggle(Toolbar.MeasureLengthTool);
                case ShortcutAction.NightMode: return Toggle(Toolbar.NightTool);

                case ShortcutAction.Markup: return Toggle(Toolbar.MarkupTool);
                case ShortcutAction.ShowMarkup:
                    if (!Toolbar.MarkupTool.Enabled) return false;
                    Toolbar.MarkupTool.ShowMarkup = !Toolbar.MarkupTool.ShowMarkup;
                    return true;
                case ShortcutAction.PinMode: return SetMarkupMode(MarkupMode.Pin);
                case ShortcutAction.ZoneMode: return SetMarkupMode(MarkupMode.Zone);
                case ShortcutAction.RectangleMode: return SetMarkupMode(MarkupMode.Rectangle);
                case ShortcutAction.SelectMode: return SetMarkupMode(MarkupMode.Select);

                // These open a window and turn themselves off again
                case ShortcutAction.GoTo: return Open(Toolbar.GoToTool);
                case ShortcutAction.Export: return Open(Toolbar.ExportTool);
                case ShortcutAction.LoadWorld: return Open(Toolbar.BrowseTool);
                case ShortcutAction.RecentWorlds: return Open(Toolbar.RecentTool);
                case ShortcutAction.BlockFilter: return Open(Toolbar.FilterTool);

                case ShortcutAction.Help:
                    ShowHelp?.Invoke();
                    return ShowHelp is not null;
                case ShortcutAction.FullScreen:
                    ToggleFullScreen?.Invoke();
                    return ToggleFullScreen is not null;
                case ShortcutAction.LeaveFullScreen:
                    return LeaveFullScreen?.Invoke() ?? false;
            }
            return false;
        }

        private Point Centre => new(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2);

        // dx, dy: which way the map itself moves on the screen
        private bool Pan(int dx, int dy, int far)
        {
            double x = far == 1 ? Canvas.ActualWidth / 4 : Step, y = far == 1 ? Canvas.ActualHeight / 4 : Step;
            View.Offset = new(View.Offset.X + dx * x, View.Offset.Y + dy * y);
            return true;
        }

        private bool StepStyle(int by)
        {
            IReadOnlyList<IStyle> styles = Styles.Styles;
            if (styles.Count == 0) return false;

            int index = Math.Max(0, styles.ToList().FindIndex(s => s.Id == Styles.SelectedStyleId));
            Styles.SelectedStyleId = styles[(index + by + styles.Count) % styles.Count].Id;
            return true;
        }

        private static bool Toggle(IToggleableTool tool)
        {
            if (!tool.Enabled) return false;
            tool.IsTurnedOn = !tool.IsTurnedOn;
            return true;
        }

        private static bool Open(IToggleableTool tool)
        {
            if (!tool.Enabled) return false;
            tool.IsTurnedOn = true;
            return true;
        }

        // Opens the markup bar first if it's closed, which would otherwise start it in Pin mode
        private bool SetMarkupMode(MarkupMode mode)
        {
            MarkupTool tool = Toolbar.MarkupTool;
            if (!tool.Enabled) return false;
            tool.IsTurnedOn = true;
            tool.Editor.Mode = mode;
            return true;
        }

        // WPF's and Avalonia's keys share their names
        public static ShortcutKey KeyOf(Key key) => key switch
        {
            >= Key.A and <= Key.Z => ShortcutKey.A + (key - Key.A),
            >= Key.D0 and <= Key.D9 => ShortcutKey.D0 + (key - Key.D0),
            >= Key.NumPad0 and <= Key.NumPad9 => ShortcutKey.D0 + (key - Key.NumPad0),
            Key.OemPlus or Key.Add => ShortcutKey.Plus,
            Key.OemMinus or Key.Subtract => ShortcutKey.Minus,
            Key.OemComma => ShortcutKey.Comma,
            Key.OemPeriod => ShortcutKey.Period,
            Key.OemOpenBrackets => ShortcutKey.OpenBracket,
            Key.OemCloseBrackets => ShortcutKey.CloseBracket,
            Key.OemQuestion => ShortcutKey.Question,
            Key.Up => ShortcutKey.Up,
            Key.Down => ShortcutKey.Down,
            Key.Left => ShortcutKey.Left,
            Key.Right => ShortcutKey.Right,
            Key.Escape => ShortcutKey.Escape,
            Key.F1 => ShortcutKey.F1,
            Key.F11 => ShortcutKey.F11,
            _ => ShortcutKey.None
        };
    }
}
