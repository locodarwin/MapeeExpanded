using Avalonia.Media;

namespace Mapper.Gui
{
    // The face the map's own labels use. Consolas is what the WPF GUI asks for; Linux machines rarely have it
    public static class TextFont
    {
        public static Typeface Bold { get; } = new(new FontFamily("Consolas, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono"), FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);
    }
}
