using Mapper.Gui.Model;
using System;
using WorldEditor;
#if AVALONIA
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
#else
using System.Windows.Controls.Primitives;
#endif

namespace Mapper.Gui.Controller
{
    // The Open button: a menu that browses for a world, then lists the worlds opened lately, each opening where the
    // map was left. With none yet it browses straight away. Taking a world off the list only changes the settings
    public class RecentWorldsTool : ToggleableTool
    {
        public ToolButton? Owner { get; set; }
        public BrowseTool? Browse { get; set; }
        public ShortcutKeys? Keys { get; set; }
        public WorldOpener Opener { get; }
        public MapViewer MapViewer { get; }

        private bool _isActive = false;

        public RecentWorldsTool(WorldOpener opener, MapViewer mapViewer)
        {
            Opener = opener;
            MapViewer = mapViewer;

            OnTurnedOn += TurnedOn;
        }

        // The button only ever flicks on: it opens the menu, unless the menu is open already, and turns straight back off
        private void TurnedOn(bool isTurnedOn)
        {
            if (!isTurnedOn) return;
            IsTurnedOn = false;
            if (_isActive) return;

            if (Opener.Settings.RecentWorlds.Count == 0 && Browse is not null)
            {
                Browse.IsTurnedOn = true;
                return;
            }

            _isActive = true;
            OpenMenu();
        }

        private void OpenMenu()
        {
            if (Owner?.Button is null)
            {
                _isActive = false;
                return;
            }

            AppSettings settings = Opener.Settings;
            RecentWorldsMenu menu = new(settings.RecentWorlds, Opener.CurrentSource, DateTime.UtcNow, Keys?.Map.KeyText(ShortcutAction.LoadWorld));

#if AVALONIA
            // A flyout under the button, as Go to's
            Flyout flyout = new() { Content = menu, Placement = PlacementMode.BottomEdgeAlignedLeft };
            void Close() => flyout.Hide();
            flyout.Closed += (sender, e) => _isActive = false;
            flyout.Opened += (sender, e) => menu.FocusFirst();
#else
            Popup popup = new() { Child = menu, PlacementTarget = Owner.Button, Placement = PlacementMode.Bottom, StaysOpen = false };
            void Close() => popup.IsOpen = false;
            popup.Closed += (sender, e) => _isActive = false;
            popup.Opened += (sender, e) => menu.FocusFirst();
#endif

            menu.CloseRequested += (sender, e) => Close();
            menu.BrowseRequested += (sender, e) =>
            {
                Close();
                if (Browse is not null) Browse.IsTurnedOn = true;
            };
            menu.Picked += (sender, world) =>
            {
                Close();
                Pick(world);
            };
            menu.Removed += (sender, world) =>
            {
                settings.Forget(world.Path);
                settings.Save();
                menu.Show(settings.RecentWorlds, Opener.CurrentSource, DateTime.UtcNow);
            };
            // The open world stays, so where the map is in it is still kept
            menu.Cleared += (sender, e) =>
            {
                settings.RecentWorlds.RemoveAll(world => !IsCurrent(world.Path, Opener.CurrentSource));
                settings.Save();
                menu.Show(settings.RecentWorlds, Opener.CurrentSource, DateTime.UtcNow);
            };

#if AVALONIA
            flyout.ShowAt(Owner.Button);
#else
            popup.IsOpen = true;
#endif
        }

        private async void Pick(RecentWorld world)
        {
            if (IsCurrent(world.Path, Opener.CurrentSource)) return;

            string title = RecentWorlds.Title(world);
            if (!WorldLocation.IsWorld(world.Path))
            {
                bool forget = await MessageWindow.Confirm($"{title} isn't at {world.Path} any more; it may have been moved or deleted. Take it off the recent list?",
                    "Mapee", "Take it off", "Keep it", MapViewer);
                if (!forget) return;

                Opener.Settings.Forget(world.Path);
                Opener.Settings.Save();
                return;
            }

            if (!await Opener.TryOpen(world.Path)) await MessageWindow.Show($"Couldn't open {title} as a Minecraft world.", "Mapee", MapViewer);
        }

        public static bool IsCurrent(string path, string? current) => RecentWorlds.IsSame(path, current);
    }
}
