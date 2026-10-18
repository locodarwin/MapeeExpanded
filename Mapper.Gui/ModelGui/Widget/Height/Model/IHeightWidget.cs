using System;

namespace Mapper.Gui.Model
{
    public enum HeightMode
    {
        // The style's own view, usually the top of the world
        Surface,
        // The style drawn as if the world stopped at the chosen height
        Slice,
        // The floors of the caves below the chosen height
        Caves
    }

    public interface IHeightWidget
    {
        bool Enabled { get; }
        int MinY { get; }
        int MaxY { get; }

        HeightMode Mode { get; set; }
        int Y { get; set; }

        // Any of the above changed, including by switching world or dimension
        event EventHandler? Changed;
    }
}
