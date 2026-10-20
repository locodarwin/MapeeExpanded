namespace Mapper
{
    // The keys the GUIs listen for, named apart from WPF's and Avalonia's own key types so the table is shared and
    // testable. Each GUI turns its keys into these (both number rows and the number pad give the digits)
    public enum ShortcutKey
    {
        None,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        Plus, Minus, Comma, Period, OpenBracket, CloseBracket, Question,
        Up, Down, Left, Right,
        Escape, F1, F11
    }

    [Flags]
    public enum ShortcutModifiers { None = 0, Control = 1, Shift = 2 }

    public enum ShortcutAction
    {
        // Argument: the dimension's place in the dimension buttons, then the custom dimensions, from 0
        Dimension,
        ZoomIn, ZoomOut, ResetZoom,
        // Argument: 0 for a small step, 1 for a quarter of the screen
        PanUp, PanDown, PanLeft, PanRight,
        PreviousStyle, NextStyle,
        // Argument for HeightDown and HeightUp: how many blocks
        HeightMode, HeightDown, HeightUp,
        Grid, SlimeChunks, Axes, ChunkHighlight, MeasureLength, NightMode,
        Markup, ShowMarkup, PinMode, ZoneMode, RectangleMode, SelectMode,
        GoTo, Export, LoadWorld, BlockFilter,
        Help, FullScreen, LeaveFullScreen
    }

    public record KeyBinding(ShortcutKey Key, ShortcutModifiers Modifiers, ShortcutAction Action, int Argument = 0);

    // A line of the key list: the keys as shown, and what they do
    public record ShortcutHelp(string Keys, string Description);
    public record ShortcutHelpGroup(string Title, IReadOnlyList<ShortcutHelp> Lines);

    // Which key does what. One fixed table for now; reading changes to it from the settings would start here
    public class KeyMap
    {
        public static KeyMap Default { get; } = new(DefaultBindings());

        public IReadOnlyList<KeyBinding> Bindings { get; }

        public KeyMap(IEnumerable<KeyBinding> bindings)
        {
            Bindings = [.. bindings];
        }

        public KeyBinding? Find(ShortcutKey key, ShortcutModifiers modifiers) =>
            Bindings.FirstOrDefault(b => b.Key == key && b.Modifiers == modifiers);

        // The first key for an action, as a tooltip shows it ("G", "Ctrl+G"), or null when it has none; argument narrows
        // it to one dimension or step
        public string? KeyText(ShortcutAction action, int? argument = null)
        {
            KeyBinding? binding = Bindings.FirstOrDefault(b => b.Action == action && (argument is null || b.Argument == argument));
            return binding is null ? null : Describe(binding.Key, binding.Modifiers);
        }

        // A tooltip with its key after it, if it has one: "Grid lines (G)"
        public string WithKey(string text, ShortcutAction action) => KeyText(action) is string key ? $"{text} ({key})" : text;

        public static string Describe(ShortcutKey key, ShortcutModifiers modifiers)
        {
            string name = key switch
            {
                >= ShortcutKey.D0 and <= ShortcutKey.D9 => ((int)(key - ShortcutKey.D0)).ToString(),
                ShortcutKey.Plus => "+",
                ShortcutKey.Minus => "−",
                ShortcutKey.Comma => ",",
                ShortcutKey.Period => ".",
                ShortcutKey.OpenBracket => "[",
                ShortcutKey.CloseBracket => "]",
                ShortcutKey.Question => "?",
                ShortcutKey.Escape => "Esc",
                _ => key.ToString()
            };
            // ? is typed with Shift already, so it isn't named
            if (modifiers.HasFlag(ShortcutModifiers.Shift) && key != ShortcutKey.Question) name = "Shift+" + name;
            if (modifiers.HasFlag(ShortcutModifiers.Control)) name = "Ctrl+" + name;
            return name;
        }

        // The key list, in the order it's shown. fullScreen: whether this GUI has a full-screen key
        public IReadOnlyList<ShortcutHelpGroup> Help(bool fullScreen)
        {
            string Keys(params ShortcutAction[] actions) => string.Join("  ", actions.Select(a => KeyText(a)).OfType<string>());

            List<ShortcutHelp> window =
            [
                new(Keys(ShortcutAction.GoTo), "Go to a place"),
                new(Keys(ShortcutAction.Export), "Export as image or web map"),
                new(Keys(ShortcutAction.LoadWorld), "Load a world"),
                new(Keys(ShortcutAction.BlockFilter), "Filter blocks"),
            ];
            if (fullScreen) window.Add(new(Keys(ShortcutAction.FullScreen), "Full screen (Esc leaves it)"));
            window.Add(new($"{KeyText(ShortcutAction.Help)}  ?", "This list, also on the toolbar's keyboard button"));

            return
            [
                new("Map",
                [
                    new("Arrows", "Move the map; with Shift, a quarter of the screen"),
                    new($"{KeyText(ShortcutAction.ZoomIn)}  {KeyText(ShortcutAction.ZoomOut)}", "Zoom in, out"),
                    new($"{KeyText(ShortcutAction.ResetZoom)}  Ctrl+R", "Back to 100%"),
                    new("1  2  3", "Overworld, Nether, End"),
                    new("4 – 9", "Custom dimensions, in their list's order"),
                    new(Keys(ShortcutAction.PreviousStyle, ShortcutAction.NextStyle), "Previous, next style"),
                ]),
                new("Height panel",
                [
                    new(Keys(ShortcutAction.HeightMode), "Surface, Slice, Caves in turn"),
                    new(Keys(ShortcutAction.HeightDown, ShortcutAction.HeightUp), "Height down, up; with Shift, by 10"),
                ]),
                new("Overlays and tools",
                [
                    new(Keys(ShortcutAction.Grid), "Grid lines"),
                    new(Keys(ShortcutAction.SlimeChunks), "Slime chunks"),
                    new(Keys(ShortcutAction.Axes), "Axes"),
                    new(Keys(ShortcutAction.ChunkHighlight), "Chunk highlighter"),
                    new(Keys(ShortcutAction.MeasureLength), "Measure length"),
                    new(Keys(ShortcutAction.NightMode), "Night mode"),
                ]),
                new("Markup",
                [
                    new(Keys(ShortcutAction.Markup), "Markup bar"),
                    new(Keys(ShortcutAction.ShowMarkup), "Show or hide pins and zones"),
                    new(Keys(ShortcutAction.PinMode, ShortcutAction.ZoneMode, ShortcutAction.RectangleMode, ShortcutAction.SelectMode), "Pin, Zone, Rectangle, Select"),
                    new("Ctrl+Z  Ctrl+Y", "Undo, redo (while the bar is open)"),
                ]),
                new("Windows", window)
            ];
        }

        public static IEnumerable<KeyBinding> DefaultBindings()
        {
            const ShortcutModifiers none = ShortcutModifiers.None, shift = ShortcutModifiers.Shift, control = ShortcutModifiers.Control;

            for (int i = 0; i < 9; i++) yield return new(ShortcutKey.D1 + i, none, ShortcutAction.Dimension, i);

            // + is Shift+= on most keyboards, so the key works with and without Shift
            yield return new(ShortcutKey.Plus, none, ShortcutAction.ZoomIn);
            yield return new(ShortcutKey.Plus, shift, ShortcutAction.ZoomIn);
            yield return new(ShortcutKey.Minus, none, ShortcutAction.ZoomOut);
            yield return new(ShortcutKey.D0, none, ShortcutAction.ResetZoom);
            yield return new(ShortcutKey.R, control, ShortcutAction.ResetZoom);

            yield return new(ShortcutKey.Up, none, ShortcutAction.PanUp);
            yield return new(ShortcutKey.Down, none, ShortcutAction.PanDown);
            yield return new(ShortcutKey.Left, none, ShortcutAction.PanLeft);
            yield return new(ShortcutKey.Right, none, ShortcutAction.PanRight);
            yield return new(ShortcutKey.Up, shift, ShortcutAction.PanUp, 1);
            yield return new(ShortcutKey.Down, shift, ShortcutAction.PanDown, 1);
            yield return new(ShortcutKey.Left, shift, ShortcutAction.PanLeft, 1);
            yield return new(ShortcutKey.Right, shift, ShortcutAction.PanRight, 1);

            yield return new(ShortcutKey.Comma, none, ShortcutAction.PreviousStyle);
            yield return new(ShortcutKey.Period, none, ShortcutAction.NextStyle);

            yield return new(ShortcutKey.H, none, ShortcutAction.HeightMode);
            yield return new(ShortcutKey.OpenBracket, none, ShortcutAction.HeightDown, 1);
            yield return new(ShortcutKey.CloseBracket, none, ShortcutAction.HeightUp, 1);
            yield return new(ShortcutKey.OpenBracket, shift, ShortcutAction.HeightDown, 10);
            yield return new(ShortcutKey.CloseBracket, shift, ShortcutAction.HeightUp, 10);

            yield return new(ShortcutKey.G, none, ShortcutAction.Grid);
            yield return new(ShortcutKey.S, none, ShortcutAction.SlimeChunks);
            yield return new(ShortcutKey.A, none, ShortcutAction.Axes);
            yield return new(ShortcutKey.C, none, ShortcutAction.ChunkHighlight);
            yield return new(ShortcutKey.L, none, ShortcutAction.MeasureLength);
            yield return new(ShortcutKey.N, none, ShortcutAction.NightMode);

            yield return new(ShortcutKey.M, none, ShortcutAction.Markup);
            yield return new(ShortcutKey.M, shift, ShortcutAction.ShowMarkup);
            yield return new(ShortcutKey.P, none, ShortcutAction.PinMode);
            yield return new(ShortcutKey.Z, none, ShortcutAction.ZoneMode);
            yield return new(ShortcutKey.R, none, ShortcutAction.RectangleMode);
            yield return new(ShortcutKey.V, none, ShortcutAction.SelectMode);

            yield return new(ShortcutKey.G, control, ShortcutAction.GoTo);
            yield return new(ShortcutKey.E, control, ShortcutAction.Export);
            yield return new(ShortcutKey.O, control, ShortcutAction.LoadWorld);
            yield return new(ShortcutKey.F, control, ShortcutAction.BlockFilter);

            yield return new(ShortcutKey.F1, none, ShortcutAction.Help);
            // ? is Shift+/ on most keyboards; the key alone is taken too
            yield return new(ShortcutKey.Question, shift, ShortcutAction.Help);
            yield return new(ShortcutKey.Question, none, ShortcutAction.Help);
            yield return new(ShortcutKey.F11, none, ShortcutAction.FullScreen);
            yield return new(ShortcutKey.Escape, none, ShortcutAction.LeaveFullScreen);
        }
    }
}
