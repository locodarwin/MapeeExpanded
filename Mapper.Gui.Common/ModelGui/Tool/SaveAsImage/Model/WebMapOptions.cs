
namespace Mapper.Gui.Model
{
    public readonly struct WebMapOptions
    {
        public bool IncludeMarkers { get; init; }
        // The world's pins and zones
        public bool IncludeMarkup { get; init; }
    }
}
