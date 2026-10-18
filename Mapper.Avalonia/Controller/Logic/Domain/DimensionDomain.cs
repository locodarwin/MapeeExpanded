using Mapper.Gui.Model;
using Avalonia;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    public class DimensionDomain
    {
        public Dimension Dimension { get; set; }
        public IRenderedScene Scene { get; set; }
        public Scaling Scaling { get; set; }

        public HeightmapSettings HeightmapSettings { get; set; }
        public RenderSettings RenderSettings { get; set; }

        // Set while the height slider is on; the map is then drawn from this height down
        public HeightCut? Cut { get; set; }

        public DimensionDomain(IRenderedScene renderedScene)
        {
            Scene = renderedScene;
            Scaling = new Scaling(0, new Point(0, 0));
        }
    }

    // Caves draws the floor of the first open space below Y, lit and coloured by the Caves style; otherwise the
    // current style is drawn as if the world stopped at Y
    public readonly record struct HeightCut(short Y, bool Caves);
}
