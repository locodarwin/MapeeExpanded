namespace Mapper
{
    // The keys the GUIs listen for, named apart from WPF's and Avalonia's own key types so the table is shared and
    // testable. Each GUI turns its keys into these (both number rows and the number pad give the digits)
    public enum ShortcutKey
    {
        None,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        Plus, Minus, Comma, Period, OpenBracket, CloseBracket, Question, Semicolon, Apostrophe, Backslash, Backtick,
        Up, Down, Left, Right,
        Escape, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
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
        Grid, SlimeChunks, Axes, ChunkHighlight, MeasureLength, NightMode, LiveReload,
        Markup, ShowMarkup, PinMode, ZoneMode, RectangleMode, SelectMode,
        GoTo, FindBlocks, Export, LoadWorld, RecentWorlds, BlockFilter,
        Help, FullScreen, LeaveFullScreen
    }

    public record KeyBinding(ShortcutKey Key, ShortcutModifiers Modifiers, ShortcutAction Action, int Argument = 0)
    {
        public KeyChord Chord => new(Key, Modifiers);
    }

    // A key with the modifiers held. + and ? are typed with Shift on most keyboards, so on those Shift is dropped and
    // the key works with and without it
    public readonly record struct KeyChord
    {
        public ShortcutKey Key { get; }
        public ShortcutModifiers Modifiers { get; }

        public KeyChord(ShortcutKey key, ShortcutModifiers modifiers)
        {
            Key = key;
            Modifiers = key is ShortcutKey.Plus or ShortcutKey.Question ? modifiers & ~ShortcutModifiers.Shift : modifiers;
        }

        public KeyChord WithShift => new(Key, Modifiers | ShortcutModifiers.Shift);

        public override string ToString() => KeyMap.Describe(Key, Modifiers);

        // As the settings file keeps it: "Ctrl+Shift+O", "-" for the minus key
        public string Stored => ToString().Replace('−', '-');

        // Reads what Stored and ToString write; null for anything else
        public static KeyChord? Parse(string text)
        {
            ShortcutModifiers modifiers = ShortcutModifiers.None;
            string rest = text.Trim();
            while (true)
            {
                if (rest.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase)) modifiers |= ShortcutModifiers.Control;
                else if (rest.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase)) modifiers |= ShortcutModifiers.Shift;
                else break;
                rest = rest[(rest.IndexOf('+') + 1)..];
            }

            ShortcutKey key = rest switch
            {
                "+" => ShortcutKey.Plus,
                "-" or "−" => ShortcutKey.Minus,
                "," => ShortcutKey.Comma,
                "." => ShortcutKey.Period,
                "[" => ShortcutKey.OpenBracket,
                "]" => ShortcutKey.CloseBracket,
                "?" or "/" => ShortcutKey.Question,
                ";" => ShortcutKey.Semicolon,
                "'" => ShortcutKey.Apostrophe,
                "\\" => ShortcutKey.Backslash,
                "`" => ShortcutKey.Backtick,
                "Esc" => ShortcutKey.Escape,
                [>= '0' and <= '9'] => ShortcutKey.D0 + (rest[0] - '0'),
                _ when rest.Length > 0 && char.IsLetter(rest[0]) && Enum.TryParse(rest, true, out ShortcutKey named) && named is not (ShortcutKey.None or >= ShortcutKey.D0 and <= ShortcutKey.D9) => named,
                _ => ShortcutKey.None
            };
            return key == ShortcutKey.None ? null : new KeyChord(key, modifiers);
        }
    }

    // A line of the key list: the keys as shown, and what they do. Action is set on lines whose key can be changed
    public record ShortcutHelp(string Keys, string Description, ShortcutAction? Action = null);
    public record ShortcutHelpGroup(string Title, IReadOnlyList<ShortcutHelp> Lines);

    // Which key does what: the built-in table, or that table with the user's changes (ShortcutKeys)
    public class KeyMap
    {
        public static KeyMap Default { get; } = new(DefaultBindings());

        public IReadOnlyList<KeyBinding> Bindings { get; }

        public KeyMap(IEnumerable<KeyBinding> bindings)
        {
            Bindings = [.. bindings];
        }

        public KeyBinding? Find(ShortcutKey key, ShortcutModifiers modifiers)
        {
            KeyChord chord = new(key, modifiers);
            return Bindings.FirstOrDefault(b => b.Chord == chord);
        }

        // The first key for an action, as a tooltip shows it ("G", "Ctrl+G"), or null when it has none; argument narrows
        // it to one dimension or step
        public string? KeyText(ShortcutAction action, int? argument = null)
        {
            KeyBinding? binding = Bindings.FirstOrDefault(b => b.Action == action && (argument is null || b.Argument == argument));
            return binding is null ? null : Describe(binding.Key, binding.Modifiers);
        }

        // Every key for an action, as the key list shows them ("F1  ?"); for the height steps, the one-block keys only
        public string KeysText(ShortcutAction action) =>
            string.Join("  ", Bindings.Where(b => b.Action == action && !IsBigStep(b)).Select(b => Describe(b.Key, b.Modifiers)));

        // A tooltip with its key after it, if it has one: "Grid lines (G)"
        public string WithKey(string text, ShortcutAction action) => KeyText(action) is string key ? $"{text} ({key})" : text;

        public static bool IsBigStep(KeyBinding binding) => binding.Action is ShortcutAction.HeightDown or ShortcutAction.HeightUp && binding.Argument != 1;

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
                ShortcutKey.Semicolon => ";",
                ShortcutKey.Apostrophe => "'",
                ShortcutKey.Backslash => "\\",
                ShortcutKey.Backtick => "`",
                ShortcutKey.Escape => "Esc",
                _ => key.ToString()
            };
            // + and ? are typed with Shift already, so it isn't named
            if (modifiers.HasFlag(ShortcutModifiers.Shift) && key is not (ShortcutKey.Question or ShortcutKey.Plus)) name = "Shift+" + name;
            if (modifiers.HasFlag(ShortcutModifiers.Control)) name = "Ctrl+" + name;
            return name;
        }

        // What an action is called in the messages about changing keys
        public static string Name(ShortcutAction action) => action switch
        {
            ShortcutAction.ZoomIn => "Zoom in",
            ShortcutAction.ZoomOut => "Zoom out",
            ShortcutAction.ResetZoom => "Back to 100%",
            ShortcutAction.PreviousStyle => "Previous style",
            ShortcutAction.NextStyle => "Next style",
            ShortcutAction.HeightMode => "Height mode",
            ShortcutAction.HeightDown => "Height down",
            ShortcutAction.HeightUp => "Height up",
            ShortcutAction.Grid => "Grid lines",
            ShortcutAction.SlimeChunks => "Slime chunks",
            ShortcutAction.Axes => "Axes",
            ShortcutAction.ChunkHighlight => "Chunk highlighter",
            ShortcutAction.MeasureLength => "Measure length",
            ShortcutAction.NightMode => "Night mode",
            ShortcutAction.LiveReload => "Live reload",
            ShortcutAction.Markup => "Markup bar",
            ShortcutAction.ShowMarkup => "Show or hide pins and zones",
            ShortcutAction.PinMode => "Pin mode",
            ShortcutAction.ZoneMode => "Zone mode",
            ShortcutAction.RectangleMode => "Rectangle mode",
            ShortcutAction.SelectMode => "Select mode",
            ShortcutAction.GoTo => "Go to a place",
            ShortcutAction.Export => "Export as image or web map",
            ShortcutAction.LoadWorld => "Load a world",
            ShortcutAction.RecentWorlds => "Recent worlds",
            ShortcutAction.BlockFilter => "Filter blocks",
            ShortcutAction.FindBlocks => "Find blocks",
            ShortcutAction.Help => "Key list",
            ShortcutAction.FullScreen => "Full screen",
            ShortcutAction.Dimension => "Pick a dimension",
            ShortcutAction.LeaveFullScreen => "Leave full screen",
            _ => "Move the map"
        };

        // The key list, in the order it's shown, a line for each key that can be changed. fullScreen: whether this
        // GUI has a full-screen key
        public IReadOnlyList<ShortcutHelpGroup> Help(bool fullScreen)
        {
            ShortcutHelp Line(ShortcutAction action)
            {
                string name = action switch
                {
                    ShortcutAction.HeightMode => "Surface, Slice, Caves in turn",
                    ShortcutAction.LiveReload => "Live: redraw what the game saves",
                    ShortcutAction.Help => "This list, also on the toolbar's keyboard button",
                    ShortcutAction.FullScreen => "Full screen (Esc leaves it)",
                    _ => Name(action)
                };
                // The Shift steps follow the one-block keys, so they're only there when those keys don't use Shift
                if (action is ShortcutAction.HeightDown or ShortcutAction.HeightUp && Bindings.Any(b => b.Action == action && IsBigStep(b))) name += "; with Shift, by 10";
                return new(KeysText(action), name, action);
            }
            IReadOnlyList<ShortcutHelp> Lines(params ShortcutAction[] actions) => [.. actions.Select(Line)];

            List<ShortcutHelp> window = [.. Lines(ShortcutAction.GoTo, ShortcutAction.FindBlocks, ShortcutAction.Export, ShortcutAction.LoadWorld, ShortcutAction.RecentWorlds, ShortcutAction.BlockFilter)];
            if (fullScreen) window.Add(Line(ShortcutAction.FullScreen));
            window.Add(Line(ShortcutAction.Help));

            return
            [
                new("Map",
                [
                    new("Arrows", "Move the map; with Shift, a quarter of the screen"),
                    .. Lines(ShortcutAction.ZoomIn, ShortcutAction.ZoomOut, ShortcutAction.ResetZoom),
                    new("1  2  3", "Overworld, Nether, End"),
                    new("4 – 9", "Custom dimensions, in their list's order"),
                    .. Lines(ShortcutAction.PreviousStyle, ShortcutAction.NextStyle),
                ]),
                new("Height panel", Lines(ShortcutAction.HeightMode, ShortcutAction.HeightDown, ShortcutAction.HeightUp)),
                new("Overlays and tools", Lines(ShortcutAction.Grid, ShortcutAction.SlimeChunks, ShortcutAction.Axes, ShortcutAction.ChunkHighlight,
                    ShortcutAction.MeasureLength, ShortcutAction.NightMode, ShortcutAction.LiveReload)),
                new("Markup",
                [
                    .. Lines(ShortcutAction.Markup, ShortcutAction.ShowMarkup, ShortcutAction.PinMode, ShortcutAction.ZoneMode, ShortcutAction.RectangleMode, ShortcutAction.SelectMode),
                    new("Ctrl+Z  Ctrl+Y", "Undo, redo (while the bar is open)"),
                ]),
                new("Windows", window)
            ];
        }

        public static IEnumerable<KeyBinding> DefaultBindings()
        {
            const ShortcutModifiers none = ShortcutModifiers.None, shift = ShortcutModifiers.Shift, control = ShortcutModifiers.Control;

            for (int i = 0; i < 9; i++) yield return new(ShortcutKey.D1 + i, none, ShortcutAction.Dimension, i);

            // + is Shift+= on most keyboards; the key works with and without Shift (KeyChord)
            yield return new(ShortcutKey.Plus, none, ShortcutAction.ZoomIn);
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
            yield return new(ShortcutKey.W, none, ShortcutAction.LiveReload);

            yield return new(ShortcutKey.M, none, ShortcutAction.Markup);
            yield return new(ShortcutKey.M, shift, ShortcutAction.ShowMarkup);
            yield return new(ShortcutKey.P, none, ShortcutAction.PinMode);
            yield return new(ShortcutKey.Z, none, ShortcutAction.ZoneMode);
            yield return new(ShortcutKey.R, none, ShortcutAction.RectangleMode);
            yield return new(ShortcutKey.V, none, ShortcutAction.SelectMode);

            yield return new(ShortcutKey.G, control, ShortcutAction.GoTo);
            yield return new(ShortcutKey.F, none, ShortcutAction.FindBlocks);
            yield return new(ShortcutKey.E, control, ShortcutAction.Export);
            yield return new(ShortcutKey.O, control, ShortcutAction.LoadWorld);
            yield return new(ShortcutKey.O, control | shift, ShortcutAction.RecentWorlds);
            yield return new(ShortcutKey.F, control, ShortcutAction.BlockFilter);

            yield return new(ShortcutKey.F1, none, ShortcutAction.Help);
            // ? is Shift+/ on most keyboards
            yield return new(ShortcutKey.Question, none, ShortcutAction.Help);
            yield return new(ShortcutKey.F11, none, ShortcutAction.FullScreen);
            yield return new(ShortcutKey.Escape, none, ShortcutAction.LeaveFullScreen);
        }
    }
}
