namespace Mapper.Gui.Model
{
    // A toolbar icon as path data in a 24×24 box: Stroke is drawn as lines in the button's text colour, Fill (if any)
    // filled in it, so an icon turns gold with its button and stays sharp at any display scale
    public record ToolIcon(string Stroke, string? Fill = null)
    {
        public const double Box = 24;
        public const double Thickness = 1.8;
    }

    // Most are Lucide's (lucide.dev, ISC licence; see THIRD-PARTY-NOTICES.txt), written out as absolute path data
    // so both GUIs' parsers read them alike; Find, Slime and Chunk are drawn to match
    public static class ToolIcons
    {
        public static readonly ToolIcon Open = new("M 6 14 L 7.5 11.1 A 2 2 0 0 1 9.24 10 L 20 10 A 2 2 0 0 1 21.94 12.5 L 20.4 18.5 A 2 2 0 0 1 18.45 20 L 4 20 A 2 2 0 0 1 2 18 L 2 5 A 2 2 0 0 1 4 3 L 7.9 3 A 2 2 0 0 1 9.59 3.9 L 10.4 5.1 A 2 2 0 0 0 12.07 6 L 18 6 A 2 2 0 0 1 20 8 L 20 10");
        public static readonly ToolIcon GoTo = new("M 2 12 L 5 12 M 19 12 L 22 12 M 12 2 L 12 5 M 12 19 L 12 22 M 5 12 A 7 7 0 1 0 19 12 A 7 7 0 1 0 5 12 Z M 9 12 A 3 3 0 1 0 15 12 A 3 3 0 1 0 9 12 Z");
        public static readonly ToolIcon Find = new("M 3 10.5 A 7.5 7.5 0 1 0 18 10.5 A 7.5 7.5 0 1 0 3 10.5 Z M 21 21 L 15.8 15.8", "M 7.6 8.8 L 13.4 8.8 L 14.8 10.4 L 10.5 14.9 L 6.2 10.4 Z");
        public static readonly ToolIcon Mark = new("M 19.914 11.105 A 7.298 7.298 0 0 0 20 10 A 8 8 0 0 0 4 10 C 4 14.993 9.539 20.193 11.399 21.799 A 1 1 0 0 0 12.601 21.799 A 32 32 0 0 0 13.425 21.061 M 9 10 A 3 3 0 1 0 15 10 A 3 3 0 1 0 9 10 Z M 16 18 L 22 18 M 19 15 L 19 21");
        public static readonly ToolIcon Measure = new("M 21.3 15.3 A 2.4 2.4 0 0 1 21.3 18.7 L 18.7 21.3 A 2.4 2.4 0 0 1 15.3 21.3 L 2.7 8.7 A 2.41 2.41 0 0 1 2.7 5.3 L 5.3 2.7 A 2.41 2.41 0 0 1 8.7 2.7 Z M 14.5 12.5 L 16.5 10.5 M 11.5 9.5 L 13.5 7.5 M 8.5 6.5 L 10.5 4.5 M 17.5 15.5 L 19.5 13.5");
        public static readonly ToolIcon Overlays = new("M 12.83 2.18 A 2 2 0 0 0 11.17 2.18 L 2.6 6.08 A 1 1 0 0 0 2.6 7.91 L 11.18 11.82 A 2 2 0 0 0 12.84 11.82 L 21.42 7.92 A 1 1 0 0 0 21.42 6.09 Z M 22 17.65 L 12.83 21.81 A 2 2 0 0 1 11.17 21.81 L 2 17.65 M 22 12.65 L 12.83 16.81 A 2 2 0 0 1 11.17 16.81 L 2 12.65");
        public static readonly ToolIcon Grid = new("M 5 3 L 19 3 A 2 2 0 0 1 21 5 L 21 19 A 2 2 0 0 1 19 21 L 5 21 A 2 2 0 0 1 3 19 L 3 5 A 2 2 0 0 1 5 3 Z M 3 9 L 21 9 M 3 15 L 21 15 M 9 3 L 9 21 M 15 3 L 15 21");
        public static readonly ToolIcon Slime = new("M 7 4.5 L 17 4.5 A 3.5 3.5 0 0 1 20.5 8 L 20.5 17 A 3.5 3.5 0 0 1 17 20.5 L 7 20.5 A 3.5 3.5 0 0 1 3.5 17 L 3.5 8 A 3.5 3.5 0 0 1 7 4.5 Z M 10.5 16 L 13.5 16", "M 7 9 L 10 9 L 10 12 L 7 12 Z M 14 9 L 17 9 L 17 12 L 14 12 Z");
        public static readonly ToolIcon Axes = new("M 12 3 L 12 21 M 3 12 L 21 12 M 9 6 L 12 3 L 15 6 M 18 9 L 21 12 L 18 15");
        public static readonly ToolIcon Chunk = new("M 5 3 A 2 2 0 0 0 3 5 M 19 3 A 2 2 0 0 1 21 5 M 5 21 A 2 2 0 0 1 3 19 M 9 3 L 10 3 M 9 21 L 10 21 M 14 3 L 15 3 M 3 9 L 3 10 M 21 9 L 21 10 M 3 14 L 3 15", "M 12.5 12.5 L 16.1 21.5 L 17.5 17.9 L 21.1 16.5 Z");
        public static readonly ToolIcon Night = new("M 12 3 A 6 6 0 0 0 21 12 A 9 9 0 1 1 12 3 Z");
        public static readonly ToolIcon Live = new("M 4.9 19.1 C 1 15.2 1 8.8 4.9 4.9 M 7.8 16.2 C 5.5 13.9 5.5 10.1 7.8 7.7 M 16.2 7.8 C 18.5 10.1 18.5 13.9 16.2 16.3 M 19.1 4.9 C 23 8.8 23 15.1 19.1 19", "M 9.8 12 A 2.2 2.2 0 1 0 14.2 12 A 2.2 2.2 0 1 0 9.8 12 Z");
        public static readonly ToolIcon Filter = new("M 22 3 L 2 3 L 10 12.46 L 10 19 L 14 21 L 14 12.46 Z");
        public static readonly ToolIcon Render = new("M 21 4 L 14 4 M 10 4 L 3 4 M 21 12 L 12 12 M 8 12 L 3 12 M 21 20 L 16 20 M 12 20 L 3 20 M 14 2 L 14 6 M 8 10 L 8 14 M 16 18 L 16 22");
        public static readonly ToolIcon Export = new("M 10.3 21 L 5 21 A 2 2 0 0 1 3 19 L 3 5 A 2 2 0 0 1 5 3 L 19 3 A 2 2 0 0 1 21 5 L 21 15 L 17.9 11.9 A 2 2 0 0 0 15.086 11.914 L 6 21 M 14 19 L 17 22 L 17 16.5 M 17 22 L 20 19 M 7 9 A 2 2 0 1 0 11 9 A 2 2 0 1 0 7 9 Z");
        public static readonly ToolIcon Keys = new("M 10 8 L 10.01 8 M 12 12 L 12.01 12 M 14 8 L 14.01 8 M 16 12 L 16.01 12 M 18 8 L 18.01 8 M 6 8 L 6.01 8 M 7 16 L 17 16 M 8 12 L 8.01 12 M 4 4 L 20 4 A 2 2 0 0 1 22 6 L 22 18 A 2 2 0 0 1 20 20 L 4 20 A 2 2 0 0 1 2 18 L 2 6 A 2 2 0 0 1 4 4 Z");
        public static readonly ToolIcon Recent = new("M 3 12 A 9 9 0 1 0 12 3 A 9.75 9.75 0 0 0 5.26 5.74 L 3 8 M 3 3 L 3 8 L 8 8 M 12 7 L 12 12 L 16 14");
        public static readonly ToolIcon Chevron = new("M 6 9 L 12 15 L 18 9");
        public static readonly ToolIcon Check = new("M 20 6 L 9 17 L 4 12");
    }
}
