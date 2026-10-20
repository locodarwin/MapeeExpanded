using System.Collections.Generic;
using System.IO;

namespace Mapper.Gui.Controller
{
    // A world or markup file dropped on the window: a world opens in place of the one on show (its view and markup are
    // saved as they change, so nothing is lost), a markup file's pins and zones join the open world's. Each GUI feeds
    // its drag events in as paths
    public class WorldDrop
    {
        public WorldOpener Opener { get; }
        public MarkupTool Markup { get; }
        public MapViewer MapViewer { get; }
        public DropOverlay Overlay { get; }

        public WorldDrop(WorldOpener opener, MarkupTool markup, MapViewer mapViewer, DropOverlay overlay)
        {
            Opener = opener;
            Markup = markup;
            MapViewer = mapViewer;
            Overlay = overlay;
        }

        // paths is null when the system only says what's being dragged at the drop (some Wayland drags); whether a
        // drop would do anything
        public bool Enter(IReadOnlyList<string>? paths)
        {
            if (paths is null)
            {
                Overlay.ShowHint("Drop a world to open it, or a markup file to add its pins and zones", true);
                return true;
            }

            (string text, bool accept) = Describe(DroppedItem.Classify(paths));
            Overlay.ShowHint(text, accept);
            return accept;
        }

        public void Leave() => Overlay.Hide();

        public async void Drop(IReadOnlyList<string> paths)
        {
            DroppedItem item = DroppedItem.Classify(paths);
            Overlay.Hide();

            switch (item.Kind)
            {
                case DropKind.World:
                    if (RecentWorldsTool.IsCurrent(item.Path!, Opener.CurrentSource)) return;
                    if (!await Opener.TryOpen(item.Path!))
                    {
                        await MessageWindow.Show($"Couldn't open {RecentWorlds.NameOf(item.Path!)} as a Minecraft world.", "Mapee", MapViewer);
                    }
                    break;
                case DropKind.Markup when Markup.Enabled:
                    Overlay.ShowMessage(Markup.Import(item.Path!), true);
                    break;
                default:
                    Overlay.ShowMessage(Describe(item).Text, false);
                    break;
            }
        }

        private (string Text, bool Accept) Describe(DroppedItem item)
        {
            if (item.Kind == DropKind.Markup)
            {
                if (!Markup.Enabled) return ("Open a world first, then drop the markup file on it to add its pins and zones", false);
                string world = Opener.Scene.Domain.CurrentWorld?.Level.WorldName ?? "this world";
                return ($"Drop to add the pins and zones in {Path.GetFileName(item.Path)} to {world}", true);
            }
            if (item.Kind == DropKind.World && RecentWorldsTool.IsCurrent(item.Path!, Opener.CurrentSource)) return ($"{RecentWorlds.NameOf(item.Path!)} is already open", false);
            return (item.Why, item.Kind != DropKind.None);
        }
    }
}
