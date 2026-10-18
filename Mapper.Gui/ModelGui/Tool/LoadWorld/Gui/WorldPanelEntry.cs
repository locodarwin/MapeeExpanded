using System.Windows.Media;
using WorldEditor;

namespace Mapper.Gui
{
    public readonly struct WorldPanelEntry
    {
        public Level Level { get; }
        public ImageSource Icon { get; }

        // What to open the world from again: its folder, or the .mcworld file it came from
        public string Source { get; }

        public WorldPanelEntry(Level level, ImageSource icon, string source)
        {
            Level = level;
            Icon = icon;
            Source = source;
        }
    }
}
