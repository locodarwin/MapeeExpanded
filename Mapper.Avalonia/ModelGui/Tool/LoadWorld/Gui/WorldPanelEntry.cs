using Avalonia.Media;
using WorldEditor;

namespace Mapper.Gui
{
    public readonly struct WorldPanelEntry
    {
        public Level Level { get; }
        public IImage Icon { get; }

        public WorldPanelEntry(Level level, IImage icon)
        {
            Level = level;
            Icon = icon;
        }
    }
}
