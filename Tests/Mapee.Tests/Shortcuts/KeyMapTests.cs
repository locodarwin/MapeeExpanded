using Mapper;

namespace Mapee.Tests.Shortcuts
{
    // The key table both GUIs share: no key does two things, every action has a key, and the key list shows them
    public class KeyMapTests
    {
        private static readonly KeyMap Keys = KeyMap.Default;

        [Fact]
        public void NoKeyDoesTwoThings()
        {
            var twice = Keys.Bindings.GroupBy(b => (b.Key, b.Modifiers)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.Empty(twice);
        }

        [Fact]
        public void EveryActionHasAKey()
        {
            var missing = Enum.GetValues<ShortcutAction>().Where(a => Keys.KeyText(a) is null).ToList();
            Assert.Empty(missing);
        }

        [Fact]
        public void DigitsPickDimensionsInOrder()
        {
            for (int i = 0; i < 9; i++)
            {
                KeyBinding? binding = Keys.Find(ShortcutKey.D1 + i, ShortcutModifiers.None);
                Assert.Equal(ShortcutAction.Dimension, binding?.Action);
                Assert.Equal(i, binding?.Argument);
            }
            Assert.Equal(ShortcutAction.ResetZoom, Keys.Find(ShortcutKey.D0, ShortcutModifiers.None)?.Action);
        }

        [Fact]
        public void ModifiersMatchExactly()
        {
            Assert.Equal(ShortcutAction.Grid, Keys.Find(ShortcutKey.G, ShortcutModifiers.None)?.Action);
            Assert.Equal(ShortcutAction.GoTo, Keys.Find(ShortcutKey.G, ShortcutModifiers.Control)?.Action);
            Assert.Null(Keys.Find(ShortcutKey.G, ShortcutModifiers.Shift));
            Assert.Null(Keys.Find(ShortcutKey.G, ShortcutModifiers.Control | ShortcutModifiers.Shift));

            Assert.Equal(ShortcutAction.Markup, Keys.Find(ShortcutKey.M, ShortcutModifiers.None)?.Action);
            Assert.Equal(ShortcutAction.ShowMarkup, Keys.Find(ShortcutKey.M, ShortcutModifiers.Shift)?.Action);
            Assert.Equal(ShortcutAction.RectangleMode, Keys.Find(ShortcutKey.R, ShortcutModifiers.None)?.Action);
            Assert.Equal(ShortcutAction.ResetZoom, Keys.Find(ShortcutKey.R, ShortcutModifiers.Control)?.Action);
        }

        [Fact]
        public void ShiftedKeysWorkWithAndWithoutShift()
        {
            // + is Shift+= and ? is Shift+/ on most keyboards
            Assert.Equal(ShortcutAction.ZoomIn, Keys.Find(ShortcutKey.Plus, ShortcutModifiers.None)?.Action);
            Assert.Equal(ShortcutAction.ZoomIn, Keys.Find(ShortcutKey.Plus, ShortcutModifiers.Shift)?.Action);
            Assert.Equal(ShortcutAction.Help, Keys.Find(ShortcutKey.Question, ShortcutModifiers.None)?.Action);
            Assert.Equal(ShortcutAction.Help, Keys.Find(ShortcutKey.Question, ShortcutModifiers.Shift)?.Action);
        }

        [Fact]
        public void ShiftTakesBiggerSteps()
        {
            Assert.Equal(new KeyBinding(ShortcutKey.Up, ShortcutModifiers.Shift, ShortcutAction.PanUp, 1), Keys.Find(ShortcutKey.Up, ShortcutModifiers.Shift));
            Assert.Equal(1, Keys.Find(ShortcutKey.CloseBracket, ShortcutModifiers.None)?.Argument);
            Assert.Equal(10, Keys.Find(ShortcutKey.CloseBracket, ShortcutModifiers.Shift)?.Argument);
            Assert.Equal(10, Keys.Find(ShortcutKey.OpenBracket, ShortcutModifiers.Shift)?.Argument);
        }

        [Fact]
        public void KeysAreNamedAsTooltipsShowThem()
        {
            Assert.Equal("G", Keys.KeyText(ShortcutAction.Grid));
            Assert.Equal("Ctrl+G", Keys.KeyText(ShortcutAction.GoTo));
            Assert.Equal("Shift+M", Keys.KeyText(ShortcutAction.ShowMarkup));
            Assert.Equal("[", Keys.KeyText(ShortcutAction.HeightDown));
            Assert.Equal("Shift+[", Keys.KeyText(ShortcutAction.HeightDown, 10));
            Assert.Equal("3", Keys.KeyText(ShortcutAction.Dimension, 2));
            Assert.Equal("?", KeyMap.Describe(ShortcutKey.Question, ShortcutModifiers.Shift));
            Assert.Equal("Grid lines (G)", Keys.WithKey("Grid lines", ShortcutAction.Grid));
        }

        [Fact]
        public void KeyListNamesFullScreenOnlyWhereThereIsOne()
        {
            static IEnumerable<ShortcutHelp> Lines(bool fullScreen) => Keys.Help(fullScreen).SelectMany(g => g.Lines);

            Assert.Contains(Lines(true), l => l.Keys == "F11");
            Assert.DoesNotContain(Lines(false), l => l.Keys == "F11");
            // Every line names its keys
            Assert.All(Lines(true), l => Assert.False(string.IsNullOrWhiteSpace(l.Keys), l.Description));
        }
    }
}
