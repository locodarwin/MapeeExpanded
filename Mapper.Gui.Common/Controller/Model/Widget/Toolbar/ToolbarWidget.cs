using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
#if AVALONIA
#else
using System.Windows.Media.Imaging;
#endif

namespace Mapper.Gui.Controller
{
    public class ToolbarWidget : IToolbarWidget
    {
        public IList<IToolButtonSegment> ToolButtonSegments { get; } = new List<IToolButtonSegment>();

        // The tools that draw over the map and stay on until turned off, by the name the settings keep them under
        public IDictionary<string, IToggleableTool> Overlays { get; } = new Dictionary<string, IToggleableTool>();
        public WorldOpener WorldOpener { get; }
        public MarkupTool MarkupTool { get; private set; } = null!;
        // The rest of the buttons' tools, for the keyboard shortcuts
        public MeasureLengthTool MeasureLengthTool { get; private set; } = null!;
        public DayNightCycleTool NightTool { get; private set; } = null!;
        public GoToTool GoToTool { get; private set; } = null!;
        public ExportAsImageTool ExportTool { get; private set; } = null!;
        public FilterTool FilterTool { get; private set; } = null!;
        public BrowseTool BrowseTool { get; private set; } = null!;

        private static KeyMap Keys => KeyMap.Default;

        public MapViewer MainWindow { get; }
        public Renderer Renderer { get; }
        public ToolScene ToolScene { get; }
        public IRenderInvoker RenderInvoker { get; }

        public ToolbarWidget(MapViewer mainWindow, Renderer renderer, WorldOpener worldOpener) 
        {
            MainWindow = mainWindow;
            Renderer = renderer;
            WorldOpener = worldOpener;
            ToolScene = new ToolScene(Renderer.Scene);
            RenderInvoker = new RenderInvoker(Renderer);

            ToolButtonSegment shortSegment = new();
            shortSegment.Tools.Add(CreateGridToolButton());
            shortSegment.Tools.Add(CreateSlimeChunkButton());
            shortSegment.Tools.Add(CreateAxisButton());
            shortSegment.Tools.Add(CreateChunkHighlightsButton());
            shortSegment.Tools.Add(CreateMeasureLengthButton());
            shortSegment.Tools.Add(CreateMarkupButton());
            shortSegment.Tools.Add(CreateDayNightCycleButton());
            shortSegment.Tools.Add(CreateGoToButton());
            shortSegment.Tools.Add(CreateExportAsImageButton());

            ToolButtonSegment longSegment = new()
            {
                LeftGap = 63
            };

            longSegment.Tools.Add(CreateFilterButton());
            longSegment.Tools.Add(CreateRenderSettingsButton());
            longSegment.Tools.Add(CreateBrowseButton());

            ToolButtonSegments.Add(shortSegment);
            ToolButtonSegments.Add(longSegment);
        }

        private ToolButton CreateGridToolButton() 
        {
            GridTool tool = new(ToolScene);
            Overlays["grid"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Grid_22px.png", Keys.WithKey("Grid lines", ShortcutAction.Grid));
        }
        private ToolButton CreateSlimeChunkButton() 
        {
            SlimeChunkTool tool = new(ToolScene, new SlimeChunkChecker(Renderer.Scene.Domain));
            Overlays["slime_chunks"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Slime_22px.png", Keys.WithKey("Slime chunk viewer", ShortcutAction.SlimeChunks));
        }
        private ToolButton CreateAxisButton() 
        {
            AxisTool tool = new(ToolScene);
            Overlays["axes"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Axis_22px.png", Keys.WithKey("Cardinal (x; z) axes", ShortcutAction.Axes));
        }
        private ToolButton CreateChunkHighlightsButton()
        {
            HighlightChunkTool tool = new(ToolScene, RenderInvoker, Renderer.GraphicsCanvas);
            Overlays["chunk_highlight"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "ChunkHighlight_22px.png", Keys.WithKey("Chunk cursor highlighter", ShortcutAction.ChunkHighlight));
        }
        private ToolButton CreateMeasureLengthButton()
        {
            MeasureLengthTool tool = new(ToolScene, RenderInvoker, Renderer.GraphicsCanvas);
            MeasureLengthTool = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "MeasureLength_22px.png", Keys.WithKey("Measure length", ShortcutAction.MeasureLength));
        }
        // Both take clicks on the map, so only one is on at a time
        private ToolButton CreateMarkupButton()
        {
            MarkupTool tool = new(Renderer.Scene, WorldOpener, WorldOpener.Settings, RenderInvoker);
            MarkupTool = tool;
            tool.OnTurnedOn += isTurnedOn =>
            {
                if (isTurnedOn) MeasureLengthTool.IsTurnedOn = false;
            };
            MeasureLengthTool.OnTurnedOn += isTurnedOn =>
            {
                if (isTurnedOn) tool.IsTurnedOn = false;
            };
            return CreateButton(tool, "Markup_22px.png", Keys.WithKey("Mark the map with pins and zones", ShortcutAction.Markup));
        }
        private ToolButton CreateDayNightCycleButton() 
        {
            DayNightCycleTool tool = new(Renderer.Scene);
            NightTool = tool;
            ToolButton output = new(tool)
            {
                ToolTip = Keys.WithKey("Night mode", ShortcutAction.NightMode),
                Icon = AppImages.Load("/Resources/Image/ToolButton/DayNightCycle_22px.png")
            };

            return output;
        }
        private ToolButton CreateGoToButton()
        {
            GoToTool tool = new(Renderer.Scene, Renderer.GraphicsCanvas) { Markup = MarkupTool };
            GoToTool = tool;
            ToolButton output = new(tool)
            {
                ToolTip = Keys.WithKey("Go to position in world", ShortcutAction.GoTo),
                Icon = AppImages.Load("/Resources/Image/ToolButton/GoTo_22px.png")
            };

            tool.Owner = output;

            return output;
        }
        private ToolButton CreateExportAsImageButton()
        {
            ExportAsImageTool tool = new(Renderer, MainWindow);
            ExportTool = tool;
            ToolButton output = new(tool)
            {
                ToolTip = Keys.WithKey("Export as image or web map", ShortcutAction.Export),
                Icon = AppImages.Load("/Resources/Image/ToolButton/ExportAsImage_22px.png")
            };

            return output;
        }
        private ToolButton CreateFilterButton()
        {
            FilterTool tool = new(Renderer.Scene);
            FilterTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/Filter_22px.png"),
                Name = "Block filter",
                ToolTip = Keys.WithKey("Filter blocks", ShortcutAction.BlockFilter)
            };

            return output;
        }
        private ToolButton CreateRenderSettingsButton()
        {
            RenderSettingsTool tool = new(Renderer.Scene);
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/RenderSettings_22px.png"),
                Name = "Render settings",
                ToolTip = "Change render settings"
            };

            return output;
        }
        private ToolButton CreateBrowseButton()
        {
            BrowseTool tool = new(Renderer.Scene, MainWindow, WorldOpener);
            BrowseTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/LoadWorld_22px.png"),
                Name = "Load world ",
                ToolTip = Keys.WithKey("Load a new world", ShortcutAction.LoadWorld)
            };

            return output;
        }

        private ToolButton CreateButton(IToggleableTool tool, string iconName, string toolTip) 
        {
            ToolButton output = new(tool)
            {
                Tool = tool,
                ToolTip = toolTip,
                Icon = AppImages.Load($"/Resources/Image/ToolButton/{iconName}")
            };

            AddInvoker(tool);

            return output;
        }
        private void AddInvoker(IToggleableTool tool) 
        {
            tool.OnTurnedOn += isTurnedOn => RenderInvoker.Render();
        }
    }
}
