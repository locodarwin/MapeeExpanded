
namespace Mapper.Gui.Model
{
    public readonly struct WebMapOptions
    {
        public bool IncludeMarkers { get; init; }
        // The world's pins and zones
        public bool IncludeMarkup { get; init; }
        // Draws every region again, even where an earlier export into the folder could be kept
        public bool RedrawAll { get; init; }
    }
}
