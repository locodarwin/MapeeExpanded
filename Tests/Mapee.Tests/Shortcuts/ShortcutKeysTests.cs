using Mapper;

namespace Mapee.Tests.Shortcuts
{
    // Changing keys: only the changes are kept, a key moves from the action that had it, the fixed keys stay fixed,
    // and a hand-edited settings file can't break the table
    public class ShortcutKeysTests
    {
        private static readonly KeyChord G = new(ShortcutKey.G, ShortcutModifiers.None);
        private static readonly KeyChord ShiftG = new(ShortcutKey.G, ShortcutModifiers.Shift);

        private static HashSet<KeyBinding> Set(KeyMap map) => [.. map.Bindings];

        [Fact]
        public void UnchangedKeysAreTheBuiltInTable()
        {
            ShortcutKeys keys = new();
            Assert.Equal(Set(KeyMap.Default), Set(keys.Map));
            Assert.Null(keys.Stored());
            Assert.False(keys.AnyChanged);
        }

        [Fact]
        public void AChangeIsKeptByActionName()
        {
            ShortcutKeys keys = new();
            int changed = 0;
            keys.Changed += () => changed++;

            ShortcutChange change = keys.Assign(ShortcutAction.Grid, ShiftG);

            Assert.Null(change.Refused);
            Assert.Empty(change.Lost);
            Assert.Equal(1, changed);
            Assert.Null(keys.Map.Find(ShortcutKey.G, ShortcutModifiers.None));
            Assert.Equal(ShortcutAction.Grid, keys.Map.Find(ShortcutKey.G, ShortcutModifiers.Shift)?.Action);
            Assert.Equal("Grid lines (Shift+G)", keys.Map.WithKey("Grid lines", ShortcutAction.Grid));
            Assert.Equal(new Dictionary<string, string>() { ["Grid"] = "Shift+G" }, keys.Stored());
        }

        [Fact]
        public void AKeyInUseMovesAndTheOtherActionIsNamed()
        {
            ShortcutKeys keys = new();
            ShortcutChange change = keys.Assign(ShortcutAction.Axes, G);

            Assert.Equal([ShortcutAction.Grid], change.Lost);
            Assert.Equal("", keys.Map.KeysText(ShortcutAction.Grid));
            Assert.Equal(ShortcutAction.Axes, keys.Map.Find(ShortcutKey.G, ShortcutModifiers.None)?.Action);
            // A, Axes' old key, now does nothing
            Assert.Null(keys.Map.Find(ShortcutKey.A, ShortcutModifiers.None));
            Assert.Equal(new Dictionary<string, string>() { ["Grid"] = "", ["Axes"] = "G" }, keys.Stored());
        }

        [Fact]
        public void AnActionWithTwoKeysKeepsTheOtherOne()
        {
            ShortcutKeys keys = new();
            ShortcutChange change = keys.Assign(ShortcutAction.Grid, new(ShortcutKey.F1, ShortcutModifiers.None));

            Assert.Equal([ShortcutAction.Help], change.Lost);
            Assert.Equal("?", keys.Map.KeysText(ShortcutAction.Help));
        }

        [Theory]
        [InlineData(ShortcutKey.D1, ShortcutModifiers.None, "1 picks a dimension, which can't be changed")]
        [InlineData(ShortcutKey.Up, ShortcutModifiers.None, "Up moves the map, which can't be changed")]
        [InlineData(ShortcutKey.Left, ShortcutModifiers.Shift, "Shift+Left moves the map, which can't be changed")]
        [InlineData(ShortcutKey.Z, ShortcutModifiers.Control, "Ctrl+Z is undo or redo in the markup bar")]
        [InlineData(ShortcutKey.Escape, ShortcutModifiers.None, "Esc can't be used for a shortcut")]
        [InlineData(ShortcutKey.None, ShortcutModifiers.None, "That key can't be used for a shortcut")]
        public void FixedKeysCantBeGiven(ShortcutKey key, ShortcutModifiers modifiers, string why)
        {
            ShortcutKeys keys = new();
            ShortcutChange change = keys.Assign(ShortcutAction.Grid, new(key, modifiers));

            Assert.Equal(why, change.Refused);
            Assert.False(keys.AnyChanged);
            Assert.Equal(Set(KeyMap.Default), Set(keys.Map));
        }

        [Fact]
        public void FixedActionsCantBeChanged()
        {
            Assert.NotNull(new ShortcutKeys().Assign(ShortcutAction.PanUp, G).Refused);
            Assert.False(ShortcutKeys.IsChangeable(ShortcutAction.Dimension));
            Assert.True(ShortcutKeys.IsChangeable(ShortcutAction.FullScreen));
        }

        [Fact]
        public void HeightStepsBringTheirShiftKey()
        {
            ShortcutKeys keys = new();
            keys.Assign(ShortcutAction.HeightUp, new(ShortcutKey.K, ShortcutModifiers.None));

            Assert.Equal(new KeyBinding(ShortcutKey.K, ShortcutModifiers.None, ShortcutAction.HeightUp, 1), keys.Map.Find(ShortcutKey.K, ShortcutModifiers.None));
            Assert.Equal(new KeyBinding(ShortcutKey.K, ShortcutModifiers.Shift, ShortcutAction.HeightUp, 10), keys.Map.Find(ShortcutKey.K, ShortcutModifiers.Shift));
            Assert.Null(keys.Map.Find(ShortcutKey.CloseBracket, ShortcutModifiers.Shift));
            Assert.Equal("K", keys.Map.KeysText(ShortcutAction.HeightUp));

            // M brings Shift+M, so both markup keys go
            ShortcutChange change = keys.Assign(ShortcutAction.HeightUp, new(ShortcutKey.M, ShortcutModifiers.None));
            Assert.Equal([ShortcutAction.Markup, ShortcutAction.ShowMarkup], change.Lost.Order());

            // With Shift itself, there's no bigger step
            keys.Assign(ShortcutAction.HeightDown, new(ShortcutKey.J, ShortcutModifiers.Shift));
            Assert.Single(keys.Map.Bindings, b => b.Action == ShortcutAction.HeightDown);
            Assert.DoesNotContain(keys.Map.Help(false).SelectMany(g => g.Lines), l => l.Action == ShortcutAction.HeightDown && l.Description.Contains("Shift"));
            Assert.Contains(keys.Map.Help(false).SelectMany(g => g.Lines), l => l.Action == ShortcutAction.HeightUp && l.Description.Contains("Shift"));
        }

        [Fact]
        public void ResetTakesTheBuiltInKeyBack()
        {
            ShortcutKeys keys = new();
            keys.Assign(ShortcutAction.Axes, G);

            ShortcutChange change = keys.Reset(ShortcutAction.Grid);
            Assert.Equal([ShortcutAction.Axes], change.Lost);
            Assert.Equal("G", keys.Map.KeysText(ShortcutAction.Grid));
            Assert.Equal("", keys.Map.KeysText(ShortcutAction.Axes));
            Assert.False(keys.IsChanged(ShortcutAction.Grid));

            keys.ResetAll();
            Assert.Equal(Set(KeyMap.Default), Set(keys.Map));
            Assert.Null(keys.Stored());
        }

        [Fact]
        public void GivingAnActionItsOwnKeyIsNoChange()
        {
            ShortcutKeys keys = new();
            keys.Assign(ShortcutAction.Grid, G);
            Assert.False(keys.AnyChanged);
        }

        [Fact]
        public void StoredChangesComeBack()
        {
            ShortcutKeys keys = new();
            keys.Assign(ShortcutAction.Axes, G);
            keys.Assign(ShortcutAction.ZoomOut, new(ShortcutKey.Minus, ShortcutModifiers.Control));
            keys.Assign(ShortcutAction.Grid, new(ShortcutKey.F1, ShortcutModifiers.None));

            Dictionary<string, string> stored = keys.Stored()!;
            Assert.Equal("Ctrl+-", stored["ZoomOut"]);
            Assert.Equal("?", stored["Help"]);

            ShortcutKeys again = new(stored);
            Assert.Empty(again.Problems);
            Assert.Equal(Set(keys.Map), Set(again.Map));
        }

        [Fact]
        public void BadStoredEntriesAreSkipped()
        {
            ShortcutKeys keys = new(new Dictionary<string, string>()
            {
                ["grid"] = "Shift+G",
                ["Bogus"] = "X",
                ["PanUp"] = "K",
                ["Axes"] = "1",
                ["NightMode"] = "Hyper+N",
            });

            Assert.Equal(4, keys.Problems.Count);
            Assert.Equal("Shift+G", keys.Map.KeysText(ShortcutAction.Grid));
            Assert.Equal("A", keys.Map.KeysText(ShortcutAction.Axes));
            Assert.Equal("N", keys.Map.KeysText(ShortcutAction.NightMode));
            Assert.Equal("Up", keys.Map.KeyText(ShortcutAction.PanUp));
        }

        [Fact]
        public void HandEditedCollisionsLeaveOneActionPerKey()
        {
            // A stored key another action has built in takes it; two stored changes on one key: the first keeps it
            ShortcutKeys keys = new(new Dictionary<string, string>() { ["Grid"] = "A", ["Markup"] = "K", ["PinMode"] = "K" });

            Assert.Equal("A", keys.Map.KeysText(ShortcutAction.Grid));
            Assert.Equal("", keys.Map.KeysText(ShortcutAction.Axes));
            Assert.Equal("K", keys.Map.KeysText(ShortcutAction.Markup));
            Assert.Equal("P", keys.Map.KeysText(ShortcutAction.PinMode));
            Assert.Single(keys.Problems);
            Assert.DoesNotContain(keys.Map.Bindings.GroupBy(b => b.Chord), g => g.Count() > 1);
        }

        [Theory]
        [InlineData("Ctrl+Shift+O", ShortcutKey.O, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
        [InlineData("shift+g", ShortcutKey.G, ShortcutModifiers.Shift)]
        [InlineData("F12", ShortcutKey.F12, ShortcutModifiers.None)]
        [InlineData("\\", ShortcutKey.Backslash, ShortcutModifiers.None)]
        [InlineData("Ctrl+−", ShortcutKey.Minus, ShortcutModifiers.Control)]
        [InlineData("Shift+?", ShortcutKey.Question, ShortcutModifiers.None)]
        [InlineData("7", ShortcutKey.D7, ShortcutModifiers.None)]
        public void ReadsKeysAsTheyAreWritten(string text, ShortcutKey key, ShortcutModifiers modifiers)
        {
            KeyChord? chord = KeyChord.Parse(text);
            Assert.Equal(new KeyChord(key, modifiers), chord);
            Assert.Equal(chord, KeyChord.Parse(chord!.Value.Stored));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Ctrl+")]
        [InlineData("Alt+G")]
        [InlineData("None")]
        [InlineData("D5")]
        [InlineData("GG")]
        public void RefusesWhatIsntAKey(string text)
        {
            Assert.Null(KeyChord.Parse(text));
        }
    }
}
