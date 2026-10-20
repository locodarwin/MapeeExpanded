using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Mapper.Gui.Logic
{
    public class Renderer
    {
        public CanvasControl GraphicsCanvas { get; }
        public Scene Scene { get; }

        public IBackgroundPainter BackgroundPainter { get; }
        public IScenePainter ScenePainter { get; }
        // Set once the toolbar exists; the image export draws markup with it
        public MarkupPainter? MarkupPainter { get; set; }

        public event RenderUpdate? OnRenderUpdate;

        private int _renderPending = 0;

        public Renderer(Scene scene, CanvasControl graphicsCanvas) 
        {
            GraphicsCanvas = graphicsCanvas;
            Scene = scene;

            BackgroundPainter = new BackgroundPainter(new TilePainter(), new SolidColorBackgroundPainter(), this);
            AddPainter(BackgroundPainter);

            ScenePainter = new ScenePainter(this);
            AddPainter(ScenePainter);

            AddScaleEvents();
            Scene.WorldBeginChange += Scene_WorldBeginChange;
            Scene.WorldChanged += Scene_WorldChanged;

            Scene.DimensionBeginChange += Scene_DimensionBeginChange;
            Scene.DimensionChanged += Scene_DimensionChanged;
        }

        public void AddPainter(IPainter painter)
        {
            GraphicsCanvas.AddPainter(painter);
        }

        // Callable from any thread. The painters run when the canvas next renders; requests made before then fold into one
        public void Render()
        {
            if (Interlocked.Exchange(ref _renderPending, 1) == 1) return;

            Dispatcher.UIThread.Post(() =>
            {
                _renderPending = 0;

                GraphicsCanvas.InvalidateVisual();
                OnRenderUpdate?.Invoke();
            }, DispatcherPriority.Render);
        }

        private void AddScaleEvents()
        {
            Scene.Map.ScaleBehaviour.OffsetChanged += ScaleBehaviour_OffsetChanged;
            Scene.Map.ScaleBehaviour.ZoomChanged += ScaleBehaviour_ZoomChanged;
        }
        private void RemoveScaleEvents()
        {
            Scene.Map.ScaleBehaviour.OffsetChanged -= ScaleBehaviour_OffsetChanged;
            Scene.Map.ScaleBehaviour.ZoomChanged -= ScaleBehaviour_ZoomChanged;
        }

        private void ScaleBehaviour_OffsetChanged(object? sender, EventArgs e)
        {
            if (Scene.Domain.CurrentWorld is not null)
            {
                Scene.Map.ScaleBehaviour.SaveScaling(Scene.Domain.CurrentWorld.CurrentDimension.Scaling);
            }

            Render();
        }
        private void ScaleBehaviour_ZoomChanged(object? sender, EventArgs e)
        {
            if (Scene.Domain.CurrentWorld is not null)
            {
                Scene.Map.ScaleBehaviour.SaveScaling(Scene.Domain.CurrentWorld.CurrentDimension.Scaling);
            }

            Render();
        }
        private void Dimension_Update(object? sender, EventArgs e)
        {
            Render();
        }

        private void Scene_WorldBeginChange(WorldDomain? old, WorldDomain current)
        {
            if (old is not null)
            {
                old.CurrentDimension.Scene.Update -= Dimension_Update;
            }

            RemoveScaleEvents();
            Render();
        }
        private void Scene_WorldChanged(WorldDomain? old, WorldDomain current)
        {
            current.CurrentDimension.Scene.Update += Dimension_Update;
            Scene.Map.ScaleBehaviour.SetScaling(current.CurrentDimension.Scaling);

            AddScaleEvents();
            Render();
        }

        private void Scene_DimensionBeginChange(DimensionDomain old, DimensionDomain current)
        {
            old.Scene.Update -= Dimension_Update;
            RemoveScaleEvents();
        }
        private void Scene_DimensionChanged(DimensionDomain old, DimensionDomain current)
        {
            Scene.Map.ScaleBehaviour.SetScaling(current.Scaling);

            current.Scene.Update += Dimension_Update;
            AddScaleEvents();
            Render();
        }
    }
}
