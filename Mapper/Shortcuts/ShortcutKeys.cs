namespace Mapper
{
    // The keys as the user has them: the built-in table with their changes on top. Only the changes are kept, by
    // action name ("Grid": "Shift+G", "" for no key, keys apart by spaces), so new shortcuts in later versions still
    // arrive. The arrows, the dimension digits and Esc can't be changed, and the Shift height steps follow the
    // one-block keys
    public class ShortcutKeys
    {
        public static readonly IReadOnlySet<ShortcutAction> Fixed = new HashSet<ShortcutAction>()
        {
            ShortcutAction.Dimension, ShortcutAction.PanUp, ShortcutAction.PanDown, ShortcutAction.PanLeft, ShortcutAction.PanRight, ShortcutAction.LeaveFullScreen
        };

        // Keys the markup bar takes while it's open, outside the key map
        private static readonly IReadOnlySet<KeyChord> Markup = new HashSet<KeyChord>()
        {
            new(ShortcutKey.Z, ShortcutModifiers.Control), new(ShortcutKey.Z, ShortcutModifiers.Control | ShortcutModifiers.Shift), new(ShortcutKey.Y, ShortcutModifiers.Control)
        };

        private static readonly IReadOnlyList<KeyBinding> FixedBindings = [.. KeyMap.DefaultBindings().Where(b => Fixed.Contains(b.Action))];
        private static readonly IReadOnlyDictionary<ShortcutAction, IReadOnlyList<KeyChord>> Defaults = Enum.GetValues<ShortcutAction>()
            .Where(IsChangeable)
            .ToDictionary(a => a, a => (IReadOnlyList<KeyChord>)[.. KeyMap.DefaultBindings().Where(b => b.Action == a && !KeyMap.IsBigStep(b)).Select(b => b.Chord)]);

        private readonly Dictionary<ShortcutAction, IReadOnlyList<KeyChord>> _changes = [];

        public KeyMap Map { get; private set; } = KeyMap.Default;
        public event Action? Changed;

        // What a settings file had that couldn't be used, for the log
        public IReadOnlyList<string> Problems { get; }

        public ShortcutKeys(IReadOnlyDictionary<string, string>? stored = null)
        {
            List<string> problems = [];
            foreach (var (name, text) in stored ?? new Dictionary<string, string>())
            {
                if (!Enum.TryParse(name, true, out ShortcutAction action) || !IsChangeable(action))
                {
                    problems.Add($"{name} isn't a shortcut that can be changed");
                    continue;
                }

                string[] parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                List<KeyChord> chords = [];
                foreach (string part in parts)
                {
                    if (KeyChord.Parse(part) is KeyChord chord && Refusal(chord) is null && !chords.Contains(chord)) chords.Add(chord);
                    else problems.Add($"{name}: {part} isn't a key that can be used");
                }
                if (chords.Count < parts.Length) continue;
                _changes[action] = chords;
            }

            // Two changes wanting one key, from a hand-edited file: the later keeps its built-in keys
            HashSet<KeyChord> taken = [];
            foreach (ShortcutAction action in _changes.Keys.Order().ToList())
            {
                List<KeyChord> claimed = [.. _changes[action].SelectMany(c => Claimed(action, c))];
                if (claimed.Any(taken.Contains))
                {
                    problems.Add($"{action}: {string.Join(" ", _changes[action])} is already used by another change");
                    _changes.Remove(action);
                    continue;
                }
                taken.UnionWith(claimed);
            }
            // A built-in key a change took is gone from the action that had it, as if changed here
            foreach (ShortcutAction other in Defaults.Keys.Where(a => !_changes.ContainsKey(a)).ToList())
            {
                List<KeyChord> kept = [.. Defaults[other].Where(c => !Claimed(other, c).Any(taken.Contains))];
                if (kept.Count < Defaults[other].Count) _changes[other] = kept;
            }

            Problems = problems;
            Map = Build();
        }

        public static bool IsChangeable(ShortcutAction action) => !Fixed.Contains(action);

        public bool IsChanged(ShortcutAction action) => _changes.ContainsKey(action);
        public bool AnyChanged => _changes.Count > 0;

        public IReadOnlyList<KeyChord> ChordsOf(ShortcutAction action) => _changes.TryGetValue(action, out var chords) ? chords : Defaults[action];

        // Why a key can't be given to an action, or null when it can
        public static string? Refusal(KeyChord chord)
        {
            if (chord.Key == ShortcutKey.None) return "That key can't be used for a shortcut";
            if (chord.Key == ShortcutKey.Escape) return "Esc can't be used for a shortcut";
            if (FixedBindings.FirstOrDefault(b => b.Chord == chord) is KeyBinding fixedBinding)
            {
                return $"{chord} {(fixedBinding.Action == ShortcutAction.Dimension ? "picks a dimension" : "moves the map")}, which can't be changed";
            }
            if (Markup.Contains(chord)) return $"{chord} is undo or redo in the markup bar";
            return null;
        }

        // Gives the action this key in place of its own. An action that had the key loses it and is named in the
        // result, as is one that had the Shift step it brings
        public ShortcutChange Assign(ShortcutAction action, KeyChord chord) => Set(action, [chord]);

        // Gives the action its built-in keys back, taking them from whatever has them now
        public ShortcutChange Reset(ShortcutAction action) => Set(action, Defaults[action]);

        public void ResetAll()
        {
            if (_changes.Count == 0) return;
            _changes.Clear();
            Map = Build();
            Changed?.Invoke();
        }

        // For the settings file; null when nothing is changed
        public Dictionary<string, string>? Stored() => _changes.Count == 0 ? null :
            _changes.OrderBy(c => c.Key).ToDictionary(c => c.Key.ToString(), c => string.Join(" ", c.Value.Select(k => k.Stored)));

        private ShortcutChange Set(ShortcutAction action, IReadOnlyList<KeyChord> chords)
        {
            if (!IsChangeable(action)) return new(null, [], $"{KeyMap.Name(action)} can't be changed");
            List<KeyChord> claimed = [.. chords.SelectMany(c => Claimed(action, c))];
            if (claimed.Select(Refusal).FirstOrDefault(r => r is not null) is string refusal) return new(null, [], refusal);

            List<ShortcutAction> lost = [];
            foreach (ShortcutAction other in Defaults.Keys.Where(a => a != action))
            {
                List<KeyChord> kept = [.. ChordsOf(other).Where(c => !Claimed(other, c).Any(claimed.Contains))];
                if (kept.Count == ChordsOf(other).Count) continue;
                Keep(other, kept);
                lost.Add(other);
            }
            Keep(action, chords);

            Map = Build();
            Changed?.Invoke();
            return new(chords.FirstOrDefault(), lost, null);
        }

        private void Keep(ShortcutAction action, IReadOnlyList<KeyChord> chords)
        {
            if (chords.SequenceEqual(Defaults[action])) _changes.Remove(action);
            else _changes[action] = chords;
        }

        // The keys an action's key takes up: a height step's key brings the same key with Shift, by 10 blocks
        private static IEnumerable<KeyChord> Claimed(ShortcutAction action, KeyChord chord)
        {
            yield return chord;
            if (HasBigStep(action, chord)) yield return chord.WithShift;
        }

        private static bool HasBigStep(ShortcutAction action, KeyChord chord) =>
            action is ShortcutAction.HeightDown or ShortcutAction.HeightUp && !chord.Modifiers.HasFlag(ShortcutModifiers.Shift);

        private KeyMap Build()
        {
            List<KeyBinding> bindings = [.. FixedBindings];
            foreach (ShortcutAction action in Defaults.Keys)
            {
                int argument = action is ShortcutAction.HeightDown or ShortcutAction.HeightUp ? 1 : 0;
                foreach (KeyChord chord in ChordsOf(action))
                {
                    bindings.Add(new(chord.Key, chord.Modifiers, action, argument));
                    if (HasBigStep(action, chord)) bindings.Add(new(chord.Key, chord.Modifiers | ShortcutModifiers.Shift, action, 10));
                }
            }
            return new(bindings);
        }
    }

    // What changing a key did: the key given (none on a reset to no keys), the actions that lost it, or why it wasn't
    public record ShortcutChange(KeyChord? Given, IReadOnlyList<ShortcutAction> Lost, string? Refused);
}
