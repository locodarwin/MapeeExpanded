using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
#if AVALONIA
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
#else
using System.Windows.Controls.Primitives;
#endif

namespace Mapper.Gui.Controller
{
    // One thing the Overlays menu can draw over the map; Id is the name the settings keep it under
    public record OverlayItem(string Id, string Label, ToolIcon Icon, IToggleableTool Tool, ShortcutAction Action);

    // The Overlays button: a menu of what can be drawn over the map, each ticked on or off where it stands. The
    // button counts how many are on, and shows as on while any is
    public class OverlaysTool : ToggleableTool
    {
        public ToolButton? Owner
        {
            get => _owner;
            set
            {
                _owner = value;
                Count();
            }
        }
        private ToolButton? _owner;

        public IReadOnlyList<OverlayItem> Items { get; }
        public ShortcutKeys Keys { get; }

        private bool _isActive = false;

        public OverlaysTool(IReadOnlyList<OverlayItem> items, ShortcutKeys keys)
        {
            Items = items;
            Keys = keys;

            foreach (OverlayItem item in items) item.Tool.OnTurnedOn += isOn => Count();
            OnTurnedOn += TurnedOn;
        }

        private void Count()
        {
            if (Owner is not null) Owner.Badge = Items.Count(i => i.Tool.IsTurnedOn);
        }

        // As Recent's: the button only flicks on to open the menu
        private void TurnedOn(bool isTurnedOn)
        {
            if (!isTurnedOn) return;
            IsTurnedOn = false;
            if (_isActive || Owner?.Button is null) return;

            _isActive = true;
            OverlaysMenu menu = new(Items, Keys);

#if AVALONIA
            Flyout flyout = new() { Content = menu, Placement = PlacementMode.BottomEdgeAlignedLeft };
            menu.CloseRequested += (sender, e) => flyout.Hide();
            flyout.Closed += (sender, e) => _isActive = false;
            flyout.Opened += (sender, e) => menu.FocusFirst();
            flyout.ShowAt(Owner.Button);
#else
            Popup popup = new() { Child = menu, PlacementTarget = Owner.Button, Placement = PlacementMode.Bottom, StaysOpen = false };
            menu.CloseRequested += (sender, e) => popup.IsOpen = false;
            popup.Closed += (sender, e) => _isActive = false;
            popup.Opened += (sender, e) => menu.FocusFirst();
            popup.IsOpen = true;
#endif
        }
    }
}
