using Mapper.Gui.Model;
using System;

namespace Mapper.Gui.Controller
{
    // The toolbar's way to the key list, so it can be found without knowing F1; the GUI says how to show it
    public class ShortcutsTool : ToggleableTool
    {
        public Action? Show { get; set; }

        public ShortcutsTool()
        {
            OnTurnedOn += isTurnedOn =>
            {
                if (!isTurnedOn) return;
                IsTurnedOn = false;
                Show?.Invoke();
            };
        }
    }
}
