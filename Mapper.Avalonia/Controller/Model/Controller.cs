using Mapper.Gui.Logic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    public class Controller
    {
        public ProgramDomain Domain { get; }
        public Scene ImplementedScene { get; }
        public Renderer ImplementedRenderer { get; }

        public CanvasControl GraphicsCanvas { get; }
        public InformationWidget InformationWidget { get; }
        public ScrollbarWidget HorizontalScrollbarWidget { get; }
        public ScrollbarWidget VerticalScrollbarWidget { get; }

        public ToolbarWidget ToolbarWidget { get; private set; }
        public ZoomWidget ZoomWidget { get; private set; }
        public DimensionWidget DimensionWidget { get; private set; }
        public StylebarWidget StylebarWidget { get; private set; }

        public TextPainter TextPainter { get; }

        public MapViewer MainWindow { get; }

        public Controller() 
        {
            GraphicsCanvas = new CanvasControl();

            Domain = new ProgramDomain();
            ImplementedScene = new Scene(GraphicsCanvas, Domain);
            ImplementedRenderer = new Renderer(ImplementedScene, GraphicsCanvas);

            InformationWidget = new InformationWidget(ImplementedScene, ImplementedRenderer);
            HorizontalScrollbarWidget = new ScrollbarWidget(ImplementedScene, ImplementedRenderer, Orientation.Horizontal);
            VerticalScrollbarWidget = new ScrollbarWidget(ImplementedScene, ImplementedRenderer, Orientation.Vertical);

            MouseHook.Start(GraphicsCanvas);

            MainWindow = new MapViewer();
            MainWindow.Initialize(CreateMainWindowArgs());

            TextPainter = new TextPainter(new RenderInvoker(ImplementedRenderer), GraphicsCanvas);
            ImplementedRenderer.AddPainter(TextPainter);

            ImplementedScene.WorldBeginChange += Scene_WorldBeginChange;
            ImplementedScene.WorldChanged += Scene_WorldChanged;
            ImplementedScene.DimensionBeginChange += Scene_DimensionBeginChange;
            ImplementedScene.StyleBeginReset += Scene_StyleReset;
        }

        private MapViewerArgs CreateMainWindowArgs() 
        {
            MapViewerArgs args = new(
                GraphicsCanvas,
                new InformationControl(InformationWidget),
                new HorizontalScrollbarControl(HorizontalScrollbarWidget),
                new VerticalScrollbarControl(VerticalScrollbarWidget));

            args.Widgets.Add(CreateZoomControl());
            args.Widgets.Add(CreateStylebarControl());
            args.Widgets.Add(CreateToolbarControl());
            args.Widgets.Add(CreateDimensionControl());

            return args;
        }

        private Control CreateZoomControl() 
        {
            ZoomWidget = new ZoomWidget(ImplementedScene.Map.ScaleBehaviour, GraphicsCanvas);

            // The WPF GUI moves these in when maximized, to clear its own window frame; the system frame needs no such step
            return new ZoomControl(ZoomWidget)
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(7, 5, 0, 0)
            };
        }
        private Control CreateStylebarControl() 
        {
            StylebarWidget = new StylebarWidget(ImplementedScene);

            return new StylebarControl(StylebarWidget)
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(7, 25, 0, 0)
            };
        }
        private Control CreateToolbarControl() 
        {
            ToolbarWidget = new ToolbarWidget(MainWindow, ImplementedRenderer);

            return new ToolbarControl(ToolbarWidget)
            {
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 0, 0)
            };
        }
        private Control CreateDimensionControl() 
        {
            DimensionWidget = new DimensionWidget(ImplementedScene);

            return new DimensionControl(DimensionWidget)
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 7, 0)
            };
        }

        private void Scene_WorldBeginChange(WorldDomain? old, WorldDomain current)
        {
            TextPainter.SetText(null);

            CheckDimensionIsEmpty(current.CurrentDimension);
        }
        private void Scene_WorldChanged(WorldDomain? old, WorldDomain current)
        {
            if (current.Level.Version.Version == Version.Pre_Beta_1_2)
            {
                StylebarWidget?.SelectedStyleId = "alpha";
            }
        }

        private void Scene_DimensionBeginChange(DimensionDomain old, DimensionDomain current) 
        {
            CheckDimensionIsEmpty(current);
        }
        private void Scene_StyleReset(Logic.Style old, Logic.Style current)
        {
            if (ImplementedScene.Domain.CurrentWorld is null) return;
            CheckDimensionIsEmpty(ImplementedScene.Domain.CurrentWorld.CurrentDimension);
        }

        private void CheckDimensionIsEmpty(DimensionDomain current) 
        {
            if (!ImplementedScene.Domain.CurrentStyle.IsDimensionAllowed(current.Dimension)) 
            {
                TextPainter.SetText(new Text($"{current.Dimension.Name} dimension does not support the current selected style"));
                return;
            }

            // Bedrock worlds index in the background; check again once that's done
            if (current.Scene.SceneParameter.RegionStore is IBackgroundRegionStore { Ready.IsCompleted: false } background)
            {
                TextPainter.SetText(new Text("Reading world. This may take a moment"));
                background.Ready.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                {
                    if (ImplementedScene.Domain.CurrentWorld?.CurrentDimension == current) CheckDimensionIsEmpty(current);
                }));
                return;
            }

            IText? text = null;
            int count = current.Scene.SceneParameter.RegionStore.Count;
            if (count < 1)
            {
                text = new Text($"{current.Dimension.Name} dimension is empty");
            }
            else if (current.Dimension == Dimension.Overworld &&
                current.Scene.RenderedRegions.Count == 0 &&
                current.Scene.SceneParameter.Level.Version.Version == Version.Pre_Beta_1_2)
            {
                text = new Text("Loading Alpha chunks. This may take a moment");
            }

            TextPainter.SetText(text);
        }
    }
}
