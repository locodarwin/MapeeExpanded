using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Mapper.Gui.Controller
{
    public class InformationWidget : IInformationWidget
    {
        public CanvasControl MainControl => Renderer.GraphicsCanvas;

        public event EventHandler<SceneInformation>? InformationUpdate;
        public event EventHandler<XzPoint>? CursorUpdate;
        public event EventHandler<CursorColumn>? ColumnUpdate;

        private readonly SceneInformation _information = new();

        public Scene Scene { get; }
        public Renderer Renderer { get; }

        private readonly BlockInspector _inspector = new();
        private (int X, int Z)? _inspectedBlock;

        public InformationWidget(Scene scene, Renderer renderer) 
        {
            Scene = scene;
            Renderer = renderer;
            Renderer.OnRenderUpdate += Renderer_Update;
            Renderer.GraphicsCanvas.PointerMoved += GraphicsCanvas_MouseMove;
            Renderer.GraphicsCanvas.PointerExited += GraphicsCanvas_MouseLeave;

            _inspector.Inspected += Inspector_Inspected;

            // What's under the mouse changes with the view as well as the mouse
            Scene.WorldChanged += (old, current) => Reinspect();
            Scene.DimensionChanged += (old, current) => Reinspect();
            Scene.ProfileReset += dimension => Reinspect();
            // and zooming or keyboard panning moves the map under a still mouse
            Scene.Map.ScaleBehaviour.ZoomChanged += (sender, e) => Dispatcher.UIThread.Post(InspectUnderMouse);
            Scene.Map.ScaleBehaviour.OffsetChanged += (sender, e) => Dispatcher.UIThread.Post(InspectUnderMouse);
        }

        private void Renderer_Update() 
        {
            InformationUpdate?.Invoke(this, GetInformation());
        }
        private void GraphicsCanvas_MouseMove(object? sender, PointerEventArgs e) 
        {
            InspectAt(e.GetPosition(Renderer.GraphicsCanvas));
        }
        private void InspectUnderMouse()
        {
            if (Renderer.GraphicsCanvas.IsMouseOver) InspectAt(Renderer.GraphicsCanvas.MousePosition);
        }
        private void InspectAt(Point position)
        {
            XzPoint point = Scene.Map.TransformPointOnScreenToXz(position);
            CursorUpdate?.Invoke(this, point);
            Inspect((int)Math.Floor(point.X), (int)Math.Floor(point.Z));
        }
        private void GraphicsCanvas_MouseLeave(object? sender, PointerEventArgs e)
        {
            CursorUpdate?.Invoke(this, Scene.Map.TransformPointOnScreenToXz(e.GetPosition(Renderer.GraphicsCanvas)));
            _inspectedBlock = null;
        }

        private void Inspect(int x, int z, bool again = false)
        {
            if (!again && _inspectedBlock == (x, z)) return;
            _inspectedBlock = (x, z);

            if (Scene.Domain.CurrentWorld is not WorldDomain world) return;
            DimensionDomain dimension = world.CurrentDimension;

            (int? startY, bool caves) = Scene.GetScanStart(world, dimension);
            _inspector.Inspect(new InspectRequest(dimension.Scene.SceneParameter.RegionStore, x, z, startY, caves));
        }
        private void Reinspect()
        {
            if (_inspectedBlock is (int x, int z)) Inspect(x, z, true);
        }

        private void Inspector_Inspected(InspectRequest request, InspectedColumn? column)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // A late answer for somewhere the mouse has left
                if (_inspectedBlock != (request.X, request.Z)) return;
                ColumnUpdate?.Invoke(this, new CursorColumn() { X = request.X, Z = request.Z, Column = column });
            });
        }

        private SceneInformation GetInformation()
        {
            _information.LoadedArea = Scene.Domain.CurrentWorld is not null ? Scene.RegionLoader.LoadedArea : null;
            _information.CursorOnBlock = Scene.Map.TransformPointOnScreenToXz(Renderer.GraphicsCanvas.MousePosition);
            _information.VisibleArea = new XzRange()
            {
                TopLeftPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.TopLeftPoint),
                BottomRightPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.BottomRightPoint)
            };
            _information.LoadedRegions = Scene.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0;
            _information.WorldInformation = Scene.Domain.CurrentWorld?.Level;
            _information.CurrentDimension = Scene.Domain.CurrentWorld?.CurrentDimension.Dimension;

            return _information;
        }
    }
}
