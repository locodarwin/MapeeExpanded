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
        public FindTool FindTool { get; private set; } = null!;
        public BrowseTool BrowseTool { get; private set; } = null!;
        public ShortcutsTool ShortcutsTool { get; private set; } = null!;
        public RecentWorldsTool RecentTool { get; private set; } = null!;
        public LiveReloadTool LiveTool { get; private set; } = null!;

        // Tooltips name the buttons' keys, and follow them when they're changed
        public ShortcutKeys Keys { get; }
        private readonly List<(ToolButton Button, string Text, ShortcutAction Action)> _keyed = [];

        public MapViewer MainWindow { get; }
        public Renderer Renderer { get; }
        public ToolScene ToolScene { get; }
        public IRenderInvoker RenderInvoker { get; }

        public ToolbarWidget(MapViewer mainWindow, Renderer renderer, WorldOpener worldOpener, ShortcutKeys keys) 
        {
            Keys = keys;
            MainWindow = mainWindow;
            Renderer = renderer;
            WorldOpener = worldOpener;
            ToolScene = new ToolScene(Renderer.Scene);
            RenderInvoker = new RenderInvoker(Renderer);

            ToolButtonSegment shortSegment = new();
            shortSegment.Tools.Add(CreateShortcutsButton());
            shortSegment.Tools.Add(CreateGridToolButton());
            shortSegment.Tools.Add(CreateSlimeChunkButton());
            shortSegment.Tools.Add(CreateAxisButton());
            shortSegment.Tools.Add(CreateChunkHighlightsButton());
            shortSegment.Tools.Add(CreateMeasureLengthButton());
            shortSegment.Tools.Add(CreateMarkupButton());
            shortSegment.Tools.Add(CreateDayNightCycleButton());
            shortSegment.Tools.Add(CreateLiveButton());
            shortSegment.Tools.Add(CreateGoToButton());
            shortSegment.Tools.Add(CreateFindButton());
            shortSegment.Tools.Add(CreateExportAsImageButton());

            ToolButtonSegment longSegment = new()
            {
                LeftGap = 63
            };

            longSegment.Tools.Add(CreateFilterButton());
            longSegment.Tools.Add(CreateRenderSettingsButton());
            longSegment.Tools.Add(CreateBrowseButton());
            longSegment.Tools.Add(CreateRecentButton());

            ToolButtonSegments.Add(shortSegment);
            ToolButtonSegments.Add(longSegment);

            keys.Changed += () =>
            {
                foreach (var (button, text, action) in _keyed) button.ShowToolTip(Keys.Map.WithKey(text, action));
            };
        }

        private void Keyed(ToolButton button, string text, ShortcutAction action)
        {
            button.ToolTip = Keys.Map.WithKey(text, action);
            _keyed.Add((button, text, action));
        }

        private ToolButton CreateGridToolButton() 
        {
            GridTool tool = new(ToolScene);
            Overlays["grid"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Grid_22px.png", "Grid lines", ShortcutAction.Grid);
        }
        private ToolButton CreateSlimeChunkButton() 
        {
            SlimeChunkTool tool = new(ToolScene, new SlimeChunkChecker(Renderer.Scene.Domain));
            Overlays["slime_chunks"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Slime_22px.png", "Slime chunk viewer", ShortcutAction.SlimeChunks);
        }
        private ToolButton CreateAxisButton() 
        {
            AxisTool tool = new(ToolScene);
            Overlays["axes"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "Axis_22px.png", "Cardinal (x; z) axes", ShortcutAction.Axes);
        }
        private ToolButton CreateChunkHighlightsButton()
        {
            HighlightChunkTool tool = new(ToolScene, RenderInvoker, Renderer.GraphicsCanvas);
            Overlays["chunk_highlight"] = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "ChunkHighlight_22px.png", "Chunk cursor highlighter", ShortcutAction.ChunkHighlight);
        }
        private ToolButton CreateMeasureLengthButton()
        {
            MeasureLengthTool tool = new(ToolScene, RenderInvoker, Renderer.GraphicsCanvas);
            MeasureLengthTool = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, "MeasureLength_22px.png", "Measure length", ShortcutAction.MeasureLength);
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
            return CreateButton(tool, "Markup_22px.png", "Mark the map with pins and zones", ShortcutAction.Markup);
        }
        private ToolButton CreateDayNightCycleButton() 
        {
            DayNightCycleTool tool = new(Renderer.Scene);
            NightTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/DayNightCycle_22px.png")
            };
            Keyed(output, "Night mode", ShortcutAction.NightMode);

            return output;
        }
        private ToolButton CreateLiveButton()
        {
            LiveReloadTool tool = new(Renderer.Scene, WorldOpener, WorldOpener.Settings);
            LiveTool = tool;
            return CreateButton(tool, "Live_22px.png", "Live: redraw what the game saves while the world is open (not for .mcworld files, which are copies)", ShortcutAction.LiveReload);
        }
        private ToolButton CreateGoToButton()
        {
            GoToTool tool = new(Renderer.Scene, Renderer.GraphicsCanvas) { Markup = MarkupTool };
            GoToTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/GoTo_22px.png")
            };
            Keyed(output, "Go to position in world", ShortcutAction.GoTo);

            tool.Owner = output;

            return output;
        }
        // Its marks are drawn over the other overlays, and under the markup, whose painter comes later
        private ToolButton CreateFindButton()
        {
            FindTool tool = new(Renderer.Scene, Renderer.GraphicsCanvas, MarkupTool, LiveTool, RenderInvoker);
            FindTool = tool;
            Renderer.AddPainter(new FindPainter(tool, Renderer.GraphicsCanvas));
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/Find_22px.png")
            };
            Keyed(output, "Find blocks: diamonds, spawners, chests or any block, anywhere in the dimension", ShortcutAction.FindBlocks);

            return output;
        }
        private ToolButton CreateExportAsImageButton()
        {
            ExportAsImageTool tool = new(Renderer, MainWindow);
            ExportTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/ExportAsImage_22px.png")
            };
            Keyed(output, "Export as image or web map", ShortcutAction.Export);

            return output;
        }
        private ToolButton CreateShortcutsButton()
        {
            ShortcutsTool tool = new();
            ShortcutsTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/Shortcuts_22px.png")
            };
            Keyed(output, "Keyboard shortcuts", ShortcutAction.Help);

            return output;
        }
        private ToolButton CreateFilterButton()
        {
            FilterTool tool = new(Renderer.Scene);
            FilterTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/Filter_22px.png"),
                Name = "Block filter"
            };
            Keyed(output, "Filter blocks", ShortcutAction.BlockFilter);

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
                Name = "Load world "
            };
            Keyed(output, "Load a new world", ShortcutAction.LoadWorld);

            return output;
        }

        private ToolButton CreateRecentButton()
        {
            RecentWorldsTool tool = new(WorldOpener, MainWindow);
            RecentTool = tool;
            ToolButton output = new(tool)
            {
                Icon = AppImages.Load("/Resources/Image/ToolButton/Recent_22px.png"),
                Name = "Recent"
            };
            Keyed(output, "Worlds opened lately", ShortcutAction.RecentWorlds);
            tool.Owner = output;

            return output;
        }

        private ToolButton CreateButton(IToggleableTool tool, string iconName, string toolTip, ShortcutAction action) 
        {
            ToolButton output = new(tool)
            {
                Tool = tool,
                Icon = AppImages.Load($"/Resources/Image/ToolButton/{iconName}")
            };
            Keyed(output, toolTip, action);

            AddInvoker(tool);

            return output;
        }
        private void AddInvoker(IToggleableTool tool) 
        {
            tool.OnTurnedOn += isTurnedOn => RenderInvoker.Render();
        }
    }
}
