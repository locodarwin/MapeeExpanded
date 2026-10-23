using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mapper.Gui.Controller
{
    public class InformationWidget : IInformationWidget
    {
        public CanvasControl MainControl => Renderer.GraphicsCanvas;
        public WorldEditor.Dimension? Dimension => Scene.Domain.CurrentWorld?.CurrentDimension.Dimension;

        public event EventHandler<SceneInformation>? InformationUpdate;
        public event EventHandler<XzPoint>? CursorUpdate;
        public event EventHandler<CursorColumn>? ColumnUpdate;

        private readonly SceneInformation _information = new();

        public Scene Scene { get; }

        // Shown beside the world's name; a new note updates the bar straight away
        public LiveReloadTool? Live
        {
            get => _live;
            set
            {
                _live = value;
                if (value is not null) value.NoteChanged += (sender, e) => InformationUpdate?.Invoke(this, GetInformation());
            }
        }
        private LiveReloadTool? _live;
        public Renderer Renderer { get; }

        private readonly BlockInspector _inspector = new();
        private (int X, int Z)? _inspectedBlock;

        public InformationWidget(Scene scene, Renderer renderer) 
        {
            Scene = scene;
            Renderer = renderer;
            Renderer.OnRenderUpdate += Renderer_Update;
            Renderer.GraphicsCanvas.MouseMove += GraphicsCanvas_MouseMove;
            Renderer.GraphicsCanvas.MouseLeave += GraphicsCanvas_MouseLeave;

            _inspector.Inspected += Inspector_Inspected;

            // What's under the mouse changes with the view as well as the mouse
            Scene.WorldChanged += (old, current) => Reinspect();
            Scene.DimensionChanged += (old, current) => Reinspect();
            Scene.ProfileReset += dimension => Reinspect();
            // and zooming or keyboard panning moves the map under a still mouse
            Scene.Map.ScaleBehaviour.ZoomChanged += (sender, e) => Renderer.GraphicsCanvas.Dispatcher.BeginInvoke(InspectUnderMouse);
            Scene.Map.ScaleBehaviour.OffsetChanged += (sender, e) => Renderer.GraphicsCanvas.Dispatcher.BeginInvoke(InspectUnderMouse);
        }

        private void Renderer_Update() 
        {
            InformationUpdate?.Invoke(this, GetInformation());
        }
        private void GraphicsCanvas_MouseMove(object sender, MouseEventArgs e) 
        {
            InspectUnderMouse();
        }
        private void InspectUnderMouse()
        {
            if (!Renderer.GraphicsCanvas.IsMouseOver) return;

            XzPoint point = Scene.Map.TransformPointOnScreenToXz(Mouse.GetPosition(Renderer.GraphicsCanvas));
            CursorUpdate?.Invoke(this, point);
            Inspect((int)Math.Floor(point.X), (int)Math.Floor(point.Z));
        }
        private void GraphicsCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            CursorUpdate?.Invoke(this, Scene.Map.TransformPointOnScreenToXz(Mouse.GetPosition(Renderer.GraphicsCanvas)));
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
            Renderer.GraphicsCanvas.Dispatcher.BeginInvoke(() =>
            {
                // A late answer for somewhere the mouse has left
                if (_inspectedBlock != (request.X, request.Z)) return;
                ColumnUpdate?.Invoke(this, new CursorColumn() { X = request.X, Z = request.Z, Column = column });
            });
        }

        private SceneInformation GetInformation()
        {
            _information.LoadedArea = Scene.Domain.CurrentWorld is not null ? Scene.RegionLoader.LoadedArea : null;
            _information.CursorOnBlock = Scene.Map.TransformPointOnScreenToXz(Mouse.GetPosition(Renderer.GraphicsCanvas));
            _information.VisibleArea = new XzRange()
            {
                TopLeftPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.TopLeftPoint),
                BottomRightPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.BottomRightPoint)
            };
            _information.LoadedRegions = Scene.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0;
            _information.WorldInformation = Scene.Domain.CurrentWorld?.Level;
            _information.LiveNote = _live?.Note;
            _information.CurrentDimension = Scene.Domain.CurrentWorld?.CurrentDimension.Dimension;

            return _information;
        }
    }
}
