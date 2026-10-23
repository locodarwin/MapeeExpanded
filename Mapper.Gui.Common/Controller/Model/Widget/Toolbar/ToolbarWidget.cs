using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;

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
        // Browsing has no button of its own: the Open menu leads to it, and Ctrl+O
        public BrowseTool BrowseTool { get; private set; } = null!;
        public ShortcutsTool ShortcutsTool { get; private set; } = null!;
        public RecentWorldsTool RecentTool { get; private set; } = null!;
        public LiveReloadTool LiveTool { get; private set; } = null!;
        public OverlaysTool OverlaysTool { get; private set; } = null!;

        public bool ShowLabels
        {
            get => WorldOpener.Settings.ToolbarLabels;
            set
            {
                WorldOpener.Settings.ToolbarLabels = value;
                WorldOpener.Settings.Save();
            }
        }

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

            // Live first, since the overlays read again what it redraws. Then the buttons, made in the order the
            // painters draw and the tools need each other: the overlays under the measure line, Find's marks over
            // both, and the markup (painted by the GUI) over everything
            LiveTool = new LiveReloadTool(Renderer.Scene, WorldOpener, WorldOpener.Settings);
            ToolButton overlays = CreateOverlaysButton();
            ToolButton measure = CreateMeasureLengthButton();
            ToolButton markup = CreateMarkupButton();
            ToolButton night = CreateDayNightCycleButton();
            ToolButton live = CreateLiveButton();
            ToolButton goTo = CreateGoToButton();
            ToolButton find = CreateFindButton();

            // Shown in the order they tend to be wanted: a world, getting around it, marking it, what's drawn over
            // it, how it's drawn, taking it away, and help
            Group(CreateOpenButton());
            Group(goTo, find);
            Group(markup, measure);
            Group(overlays, night, live);
            Group(CreateFilterButton(), CreateRenderSettingsButton());
            Group(CreateExportAsImageButton());
            Group(CreateShortcutsButton());

            keys.Changed += () =>
            {
                foreach (var (button, text, action) in _keyed) button.ShowToolTip(Keys.Map.WithKey(text, action));
            };
        }

        private void Group(params ToolButton[] buttons)
        {
            ToolButtonSegment segment = new();
            foreach (ToolButton button in buttons) segment.Tools.Add(button);
            ToolButtonSegments.Add(segment);
        }

        private void Keyed(ToolButton button, string text, ShortcutAction action)
        {
            button.ToolTip = Keys.Map.WithKey(text, action);
            _keyed.Add((button, text, action));
        }

        // Grid, slime chunks, axes, the chunk highlight, where mobs can spawn and the Nether portals, behind one button
        private ToolButton CreateOverlaysButton()
        {
            OverlayItem Overlay(string id, string label, ToolIcon icon, IToggleableTool tool, ShortcutAction action)
            {
                Overlays[id] = tool;
                Renderer.AddPainter((IPainter)tool);
                AddInvoker(tool);
                return new(id, label, icon, tool, action);
            }

            OverlaysTool tool = new([
                Overlay("grid", "Grid lines", ToolIcons.Grid, new GridTool(ToolScene), ShortcutAction.Grid),
                Overlay("slime_chunks", "Slime chunks", ToolIcons.Slime, new SlimeChunkTool(ToolScene, new SlimeChunkChecker(Renderer.Scene.Domain)), ShortcutAction.SlimeChunks),
                Overlay("axes", "Axes (x and z)", ToolIcons.Axes, new AxisTool(ToolScene), ShortcutAction.Axes),
                Overlay("chunk_highlight", "Chunk under the cursor", ToolIcons.Chunk, new HighlightChunkTool(ToolScene, RenderInvoker, Renderer.GraphicsCanvas), ShortcutAction.ChunkHighlight),
                Overlay("spawn_spots", "Where mobs can spawn", ToolIcons.Spawn, new SpawnTool(ToolScene, new SpawnSpots(Renderer.Scene, LiveTool), RenderInvoker), ShortcutAction.SpawnSpots),
                Overlay("portals", "Nether portals", ToolIcons.Portal, new PortalTool(Renderer.Scene, new NetherPortals(Renderer.Scene, LiveTool), Renderer.GraphicsCanvas, RenderInvoker), ShortcutAction.Portals)
            ], Keys);
            OverlaysTool = tool;

            ToolButton output = new(tool) { Icon = ToolIcons.Overlays, Name = "Overlays", Menu = true };
            output.ToolTip = "Draw over the map: grid lines, slime chunks, axes, the chunk under the cursor, where mobs can spawn, Nether portals";
            tool.Owner = output;

            return output;
        }
        private ToolButton CreateMeasureLengthButton()
        {
            MeasureLengthTool tool = new(ToolScene, RenderInvoker, Renderer.GraphicsCanvas);
            MeasureLengthTool = tool;
            Renderer.AddPainter(tool);
            return CreateButton(tool, ToolIcons.Measure, "Measure", "Measure length", ShortcutAction.MeasureLength);
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
            return CreateButton(tool, ToolIcons.Mark, "Mark", "Mark the map with pins and zones", ShortcutAction.Markup);
        }
        private ToolButton CreateDayNightCycleButton()
        {
            DayNightCycleTool tool = new(Renderer.Scene);
            NightTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.Night, Name = "Night" };
            Keyed(output, "Night mode", ShortcutAction.NightMode);

            return output;
        }
        private ToolButton CreateLiveButton()
        {
            return CreateButton(LiveTool, ToolIcons.Live, "Live", "Live: redraw what the game saves while the world is open (not for .mcworld files, which are copies)", ShortcutAction.LiveReload);
        }
        private ToolButton CreateGoToButton()
        {
            GoToTool tool = new(Renderer.Scene, Renderer.GraphicsCanvas) { Markup = MarkupTool };
            GoToTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.GoTo, Name = "Go to" };
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
            ToolButton output = new(tool) { Icon = ToolIcons.Find, Name = "Find" };
            Keyed(output, "Find blocks: diamonds, spawners, chests or any block, anywhere in the dimension", ShortcutAction.FindBlocks);

            return output;
        }
        private ToolButton CreateExportAsImageButton()
        {
            ExportAsImageTool tool = new(Renderer, MainWindow);
            ExportTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.Export, Name = "Export" };
            Keyed(output, "Export as image or web map", ShortcutAction.Export);

            return output;
        }
        private ToolButton CreateShortcutsButton()
        {
            ShortcutsTool tool = new();
            ShortcutsTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.Keys, Name = "Keys" };
            Keyed(output, "Keyboard shortcuts", ShortcutAction.Help);

            return output;
        }
        private ToolButton CreateFilterButton()
        {
            FilterTool tool = new(Renderer.Scene);
            FilterTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.Filter, Name = "Filter" };
            Keyed(output, "Filter blocks", ShortcutAction.BlockFilter);

            return output;
        }
        private ToolButton CreateRenderSettingsButton()
        {
            RenderSettingsTool tool = new(Renderer.Scene);
            return new(tool)
            {
                Icon = ToolIcons.Render,
                Name = "Render",
                ToolTip = "Render settings: light, height offset, background"
            };
        }
        // Browse first, then the worlds opened lately; with none yet, straight to browsing
        private ToolButton CreateOpenButton()
        {
            BrowseTool = new BrowseTool(Renderer.Scene, MainWindow, WorldOpener);
            RecentWorldsTool tool = new(WorldOpener, MainWindow) { Browse = BrowseTool, Keys = Keys };
            RecentTool = tool;
            ToolButton output = new(tool) { Icon = ToolIcons.Open, Name = "Open", Menu = true };
            Keyed(output, "Open a world: browse, or pick one opened lately", ShortcutAction.RecentWorlds);
            tool.Owner = output;

            return output;
        }

        private ToolButton CreateButton(IToggleableTool tool, ToolIcon icon, string name, string toolTip, ShortcutAction action)
        {
            ToolButton output = new(tool) { Icon = icon, Name = name };
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
