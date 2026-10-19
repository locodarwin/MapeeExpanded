using Mapper.Gui.Logic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using System.Linq;
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
        public HeightWidget HeightWidget { get; private set; }

        public TextPainter TextPainter { get; }

        public MapViewer MainWindow { get; }

        public AppSettings Settings { get; }
        public WorldOpener WorldOpener { get; }

        private bool _automaticStyle;

        // startupWorld, if given, opens in place of the world browser
        public Controller(AppSettings? settings = null, string? startupWorld = null) 
        {
            Settings = settings ?? AppSettings.Load();
            StartupLog.Mark("Settings read");
            GraphicsCanvas = new CanvasControl();

            Domain = new ProgramDomain();
            StartupLog.Mark("Styles and assets read");
            ImplementedScene = new Scene(GraphicsCanvas, Domain);
            ImplementedRenderer = new Renderer(ImplementedScene, GraphicsCanvas);
            WorldOpener = new WorldOpener(ImplementedScene, Settings) { StartupWorld = startupWorld };

            InformationWidget = new InformationWidget(ImplementedScene, ImplementedRenderer);
            HorizontalScrollbarWidget = new ScrollbarWidget(ImplementedScene, ImplementedRenderer, Orientation.Horizontal);
            VerticalScrollbarWidget = new ScrollbarWidget(ImplementedScene, ImplementedRenderer, Orientation.Vertical);

            MouseHook.Start(GraphicsCanvas);

            StartupLog.Mark("Scene and renderer made");
            MainWindow = new MapViewer();
            StartupLog.Mark("Main window made");
            MainWindow.Initialize(CreateMainWindowArgs());
            MainWindow.ApplySettings(Settings.Window);
            WorldOpener.DimensionWidget = DimensionWidget;

            TextPainter = new TextPainter(new RenderInvoker(ImplementedRenderer), GraphicsCanvas);
            ImplementedRenderer.AddPainter(TextPainter);

            ImplementedScene.WorldBeginChange += Scene_WorldBeginChange;
            ImplementedScene.WorldChanged += Scene_WorldChanged;
            ImplementedScene.DimensionBeginChange += Scene_DimensionBeginChange;
            ImplementedScene.StyleBeginReset += Scene_StyleReset;

            ApplySettings();
            StartupLog.Mark("Main window ready");
        }

        // The last style and overlays come back, and every change is saved as it happens, so a crash loses nothing
        private void ApplySettings()
        {
            if (Settings.Style is string style && Domain.Styles.Any(s => s.Metadata.Id == style))
            {
                StylebarWidget.SelectedStyleId = style;
            }
            // The style bar reports a change even with no world open, unlike the scene
            StylebarWidget.StyleCollectionChanged += (sender, style) =>
            {
                if (_automaticStyle) return;
                Settings.Style = style.Id;
                Settings.Save();
            };

            foreach (var (name, tool) in ToolbarWidget.Overlays)
            {
                tool.IsTurnedOn = Settings.IsOverlayOn(name);
                tool.OnTurnedOn += isTurnedOn =>
                {
                    Settings.SetOverlay(name, isTurnedOn);
                    Settings.Save();
                };
            }

            MainWindow.Closing += (sender, e) =>
            {
                WorldOpener.RememberView();
                MainWindow.SaveSettings(Settings.Window);
                Settings.Save();
            };
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
            args.Widgets.Add(CreateHeightControl());
            // Last, so it lies over the other widgets
            args.Widgets.Add(new CoordinatesTip(InformationWidget, ImplementedScene));

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
        private Control CreateHeightControl()
        {
            HeightWidget = new HeightWidget(ImplementedScene);

            // Opposite the style bar, clear of the dimension buttons above
            return new HeightControl(HeightWidget)
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 25, 7, 0)
            };
        }
        private Control CreateToolbarControl() 
        {
            ToolbarWidget = new ToolbarWidget(MainWindow, ImplementedRenderer, WorldOpener);

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
                // Alpha worlds need their own style; that's not the user's choice, so it isn't remembered
                _automaticStyle = true;
                StylebarWidget?.SelectedStyleId = "alpha";
                _automaticStyle = false;
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
                StartupLog.Mark("Map message: Reading world");
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
            StartupLog.Mark($"Map message: {text?.DisplayedText ?? "none"}");
        }
    }
}
