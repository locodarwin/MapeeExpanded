using Mapper.Gui.Model;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    public class ScenePainter : IScenePainter
    {
        public Renderer Renderer { get; }

        public ScalePainter ScalePainter { get; set; }
        public IPainter<XzRange> RegionPainter { get; set; }

        public ScenePainter(Renderer renderer) 
        {
            Renderer = renderer;

            ScalePainter = new ScalePainter();
            RegionPainter = new RegionPainter(new RegionScene(Renderer.Scene.Domain));
        }

        public void Paint(DrawingContext drawingContext)
        {
            ScenePaintArgs args = new() 
            {
                Scale = new ScalePaintArgs() 
                {
                    Enabled = Renderer.Scene.Map.ScaleBehaviour.Enabled,
                    Offset = Renderer.Scene.Map.ScaleBehaviour.Offset,
                    ZoomLevel = Renderer.Scene.Map.ScaleBehaviour.CurrentZoomLevel,
                    ZoomCoefficient = Renderer.Scene.Map.ScaleBehaviour.CurrentZoomCoefficient,
                    LevelIncrement = Renderer.Scene.Map.ScaleBehaviour.LevelIncrement,
                    RenderScaling = Renderer.GraphicsCanvas.RenderScaling
                },
                VisibleArea = Renderer.Scene.Map.ProvideVisibleGrid()
            };

            Paint(drawingContext, args);
        }
        public void Paint(DrawingContext drawingContext, ScenePaintArgs args)
        {
            using (ScalePainter.Push(drawingContext, args.Scale))
            {
                RegionPainter.Paint(drawingContext, args.VisibleArea);
            }
        }
    }
}
