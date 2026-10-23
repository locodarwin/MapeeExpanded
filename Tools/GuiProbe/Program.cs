// Runs the Avalonia GUI on Avalonia's headless platform with Skia drawing and acts out a list of steps, saving
// screenshots of every open window along the way.
//   GuiProbe <output folder> <step>...
// Settings are kept in <output folder>/Settings.json, not the user's.
// Steps:
//   load:<world folder or .mcworld>   open a world straight into the scene
//   open:<world, level.dat or .mcworld>  open a world as the browser and command line do, remembering it
//   restart[:<world>]                 close the app and start it again, optionally naming a world as on the command line
//   state                             print the style, overlays, dimension and view
//   wait:<ms>                         let the app run (regions render in the background)
//   settle                            wait until no new regions have rendered for a second
//   shot:<name>                       save <name>-<n>.png for each open window, main window first
//   size:<w>x<h>                      resize the main window
//   scale:<factor>                    render the main window as on a scaled display
//   dim:<overworld|nether|end>        switch dimension
//   style:<id>                        pick a style
//   tool:<tooltip or name>            press a toolbar button
//   zoom:<levels>                     zoom in (positive) or out (negative) around the centre
//   mouse:<x>,<y>                     move the mouse over the main window
//   info                              print what the info bar says about the block under the mouse
//   height:<surface|slice|caves>[:<y>]  set the height panel
//   drag:<x1>,<y1>,<x2>,<y2>          left-drag across the main window
//   save:[plain-]<screenshot|full>:<file>  export the map as the Export as image window would (plain: without markup)
//   webmap:<folder>                   export the dimension on screen as a web map
//   check:<control name>              tick a radio button or check box in any open window
//   close                             close every window but the main one
//   click:<x>,<y>[:right|shift|double]  click on the main window (double: two clicks in quick succession)
//   key:[ctrl+][shift+]<key>          press a key on the main window (Escape, Back, Delete, Enter, Z, ...)
//   markup:<pin|zone|rectangle|select|view>  pick what a click on the map does (turning the Markup button on first)
//   edit:<label>[|<colour hex>|<icon id>|<note>]  fill in the open pin or zone popup and save it; edit:!cancel, edit:!delete
//   marks                             print the world's pins and zones, and the Places list
//   recent                            open the Recent menu and print its rows, and how many menus are open
//   recent:<world name>               pick that row in the open Recent menu
//   recent-x:<world name>             press that row's × (take it off the list); recent-x:!clear, Clear the list
//   dragover:<path>[|<path>]          drag files over the middle of the main window and hold them there
//   dragleave                         drag them back out
//   drop:<path>[|<path>]              drag files onto the middle of the main window and drop them
//   find:<what>[,<what>][|<from y>|<to y>]  open Find blocks (as key F does), choose groups (diamonds, spawners, ...) or
//                                     blocks (diamond_ore), search, wait for it to finish and print what it found
//   find-start:<what>...              the same, without waiting for the search to finish
//   find-type:<text>                  type into the find window's block search and print the first blocks listed
//   find-pick:<row>                   pick a row of the find window's results (from 0): the map goes there
//   find-pin                          press Add a pin for the picked row
//   find-stop                         press Stop
//   find-state                        print the find window's status, counts and first rows, and the marks on screen
//   keys                              print the key list's state, the changed keys kept, and tooltips that name keys
//   keys:<action>                     click that action's key in the key list (Grid, HeightUp, ...); keys:reset:<action>,
//                                     keys:reset-all, keys:card, keys:outside (the dimmed map around it)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mapper;
using Mapper.Gui;
using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using WorldEditor;

if (args.Length < 1)
{
    Console.WriteLine("Usage: GuiProbe <output folder> <step>...");
    return 1;
}

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

List<Window> windows = [];
Window.WindowOpenedEvent.AddClassHandler<Window>((window, e) => { if (!windows.Contains(window)) windows.Add(window); });
Window.WindowClosedEvent.AddClassHandler<Window>((window, e) => windows.Remove(window));

string settingsFile = Path.Combine(output, "Settings.json");
Controller controller = null!;
MapViewer main = null!;
CursorColumn? lastColumn = null;
Start(null);

// The files held over the window by dragover, for dragleave
DataTransfer? heldDrag = null;
foreach (string step in args.Skip(1))
{
    string[] parts = step.Split(':', 2);
    string value = parts.Length > 1 ? parts[1] : "";
    Console.WriteLine($"> {step}");

    switch (parts[0])
    {
        case "load":
            CloseOthers();
            string folder = File.Exists(value) && McWorldArchive.IsArchive(value) ? McWorldArchive.Extract(value) : value;
            Level level = new AnyEditionLevelReader().Read(Path.Combine(folder, "level.dat")) ?? throw new InvalidOperationException($"No world at {value}");
            controller.ImplementedScene.SetWorld(level);
            Pump(500);
            break;
        case "open":
            CloseOthers();
            Task<bool> opening = controller.WorldOpener.TryOpen(value);
            while (!opening.IsCompleted) Pump(50);
            Console.WriteLine($"  opened: {opening.Result}");
            Pump(500);
            break;
        case "restart":
            CloseOthers();
            main.Close();
            Pump(300);
            Start(value.Length > 0 ? value : null);
            break;
        case "state":
            var world = controller.Domain.CurrentWorld;
            Console.WriteLine($"  style {controller.Domain.CurrentStyle.Metadata.Id}; overlays on: {string.Join(",", controller.ToolbarWidget.Overlays.Where(o => o.Value.IsTurnedOn).Select(o => o.Key))}; window {main.Width}x{main.Height}");
            Console.WriteLine(world is null ? "  no world" : $"  world {world.Level.WorldName}, {world.CurrentDimension.Dimension.Name}, centre {world.CurrentDimension.Scaling.CenterPoint}, zoom {world.CurrentDimension.Scaling.ZoomLevel}");
            Console.WriteLine($"  recent: {string.Join(" | ", controller.Settings.RecentWorlds.Select(w => Path.GetFileName(w.Path)))}");
            var view = controller.ImplementedScene.Map.ScaleBehaviour;
            var tools = controller.ToolbarWidget;
            Console.WriteLine($"  view zoom level {view.CurrentZoomLevel}, offset {view.Offset}; height {controller.HeightWidget.Mode} {controller.HeightWidget.Y}; " +
                $"markup {(tools.MarkupTool.IsTurnedOn ? tools.MarkupTool.Editor.Mode : "off")}, {(tools.MarkupTool.ShowMarkup ? "shown" : "hidden")}; " +
                $"measure {(tools.MeasureLengthTool.IsTurnedOn ? "on" : "off")}; night {(tools.NightTool.IsTurnedOn ? "on" : "off")}; key list {(controller.ShortcutsPanel.IsOpen ? "open" : "closed")}; live {(tools.LiveTool.IsTurnedOn ? "on" : "off")}{(tools.LiveTool.Enabled ? "" : " (unavailable)")}: {tools.LiveTool.Note ?? "-"}");
            break;
        case "wait":
            Pump(int.Parse(value));
            break;
        case "settle":
            Settle();
            break;
        case "shot":
            Shot(value);
            break;
        case "size":
            string[] size = value.Split('x');
            main.Width = double.Parse(size[0]);
            main.Height = double.Parse(size[1]);
            Pump(500);
            break;
        case "scale":
            main.SetRenderScaling(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
            Pump(500);
            break;
        case "dim":
            controller.DimensionWidget.CurrentDimension = value switch
            {
                "nether" => Dimension.Nether,
                "end" => Dimension.TheEnd,
                _ => Dimension.Overworld
            };
            Pump(500);
            break;
        case "style":
            controller.StylebarWidget.SelectedStyleId = value;
            Pump(500);
            break;
        case "tool":
            IToolButton button = controller.ToolbarWidget.ToolButtonSegments.SelectMany(segment => segment.Tools)
                .FirstOrDefault(tool => string.Equals(tool.ToolTip, value, StringComparison.OrdinalIgnoreCase) || string.Equals(tool.Name?.Trim(), value, StringComparison.OrdinalIgnoreCase) ||
                    // without the key a tooltip ends with, "Grid lines" for "Grid lines (G)"
                    tool.ToolTip?.StartsWith(value + " (", StringComparison.OrdinalIgnoreCase) == true)
                ?? throw new InvalidOperationException($"No toolbar button is called {value}; there are: {string.Join(" | ", controller.ToolbarWidget.ToolButtonSegments.SelectMany(segment => segment.Tools).Select(t => t.ToolTip))}");
            button.Button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(800);
            break;
        case "zoom":
            int levels = int.Parse(value);
            Point centre = new(controller.GraphicsCanvas.ActualWidth / 2, controller.GraphicsCanvas.ActualHeight / 2);
            if (levels > 0) controller.ImplementedScene.Map.ScaleBehaviour.ZoomIn(centre, levels);
            else controller.ImplementedScene.Map.ScaleBehaviour.ZoomOut(centre, -levels);
            Pump(500);
            break;
        case "mouse":
            main.MouseMove(ParsePoint(value), RawInputModifiers.None);
            Pump(300);
            break;
        case "info":
            Pump(300);
            Console.WriteLine(lastColumn is null ? "  no column reported" : $"  ({lastColumn.X}, {lastColumn.Z}): {Describe(lastColumn.Column)}");
            break;
        case "height":
            string[] height = value.Split(':');
            if (height.Length > 1) controller.HeightWidget.Y = int.Parse(height[1]);
            controller.HeightWidget.Mode = Enum.Parse<HeightMode>(height[0], true);
            Pump(600);
            Console.WriteLine($"  {controller.HeightWidget.Mode} at Y {controller.HeightWidget.Y} ({controller.HeightWidget.MinY} to {controller.HeightWidget.MaxY})");
            break;
        case "drag":
            double[] d = value.Split(',').Select(double.Parse).ToArray();
            main.MouseMove(new Point(d[0], d[1]), RawInputModifiers.None);
            main.MouseDown(new Point(d[0], d[1]), MouseButton.Left, RawInputModifiers.None);
            for (int i = 1; i <= 10; i++)
            {
                main.MouseMove(new Point(d[0] + (d[2] - d[0]) * i / 10, d[1] + (d[3] - d[1]) * i / 10), RawInputModifiers.LeftMouseButton);
                Pump(20);
            }
            main.MouseUp(new Point(d[2], d[3]), MouseButton.Left, RawInputModifiers.None);
            Pump(300);
            break;
        case "save":
            string[] save = value.Split(':', 2);
            IImageSaver saver = controller.ToolbarWidget.ToolButtonSegments.SelectMany(segment => segment.Tools)
                .Select(tool => tool.Tool).OfType<ExportAsImageTool>().First().ImageSaver;
            bool plain = save[0].StartsWith("plain-");
            if (save[0].EndsWith("screenshot")) saver.SaveAsScreenshot(save[1], !plain);
            else
            {
                // Off the UI thread, as the export window saves it
                Task saving = Task.Run(() => saver.SaveAsFullResolution(save[1], saver.DefaultFullResArgs with { IncludeMarkup = !plain }));
                while (!saving.IsCompleted) Pump(50);
                saving.GetAwaiter().GetResult();
            }
            Pump(200);
            Console.WriteLine($"  saved {save[1]} ({new FileInfo(save[1]).Length:N0} bytes)");
            break;
        case "webmap":
            IImageSaver webSaver = controller.ToolbarWidget.ToolButtonSegments.SelectMany(segment => segment.Tools)
                .Select(tool => tool.Tool).OfType<ExportAsImageTool>().First().ImageSaver;
            var export = webSaver.PrepareWebMap(value, new WebMapOptions() { IncludeMarkers = true, IncludeMarkup = true });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WebMapProgress last = default;
            Task exporting = Task.Run(() => export(new SynchronousProgress<WebMapProgress>(p => last = p), CancellationToken.None));
            while (!exporting.IsCompleted) Pump(100);
            exporting.GetAwaiter().GetResult();
            foreach (string dimensionFolder in Directory.GetDirectories(value).Where(d => Directory.Exists(Path.Combine(d, "tiles"))))
            {
                int tiles = Directory.GetFiles(Path.Combine(dimensionFolder, "tiles"), "*.png", SearchOption.AllDirectories).Length;
                int heights = Directory.GetFiles(Path.Combine(dimensionFolder, "heights")).Length;
                Console.WriteLine($"  {Path.GetFileName(dimensionFolder)}: {tiles} tiles in all, {heights} height files");
            }
            Console.WriteLine($"  {webSaver.WebMapDescription}: {watch.ElapsedMilliseconds} ms");
            break;
        case "check":
            ToggleButton toggle = windows.Select(window => window.FindControl<ToggleButton>(value)).FirstOrDefault(control => control is not null)
                ?? throw new ArgumentException($"No open window has a control named {value}");
            toggle.IsChecked = true;
            Pump(300);
            break;
        case "close":
            CloseOthers();
            break;
        case "click":
            string[] click = value.Split(':');
            Point at = ParsePoint(click[0]);
            string how = click.Length > 1 ? click[1] : "";
            RawInputModifiers held = how == "shift" ? RawInputModifiers.Shift : RawInputModifiers.None;
            MouseButton which = how == "right" ? MouseButton.Right : MouseButton.Left;
            for (int i = 0; i < (how == "double" ? 2 : 1); i++)
            {
                main.MouseMove(at, held);
                main.MouseDown(at, which, held);
                main.MouseUp(at, which, held);
                Pump(how == "double" ? 30 : 250);
            }
            Pump(250);
            break;
        case "key":
            string[] chord = value.Split('+');
            RawInputModifiers keyModifiers = RawInputModifiers.None;
            if (chord.Contains("ctrl")) keyModifiers |= RawInputModifiers.Control;
            if (chord.Contains("shift")) keyModifiers |= RawInputModifiers.Shift;
            main.KeyPress(Enum.Parse<Key>(chord[^1], true), keyModifiers, PhysicalKey.None, null);
            main.KeyRelease(Enum.Parse<Key>(chord[^1], true), keyModifiers, PhysicalKey.None, null);
            Pump(250);
            break;
        case "markup":
            MarkupTool markup = controller.ToolbarWidget.MarkupTool;
            MarkupMode mode = Enum.Parse<MarkupMode>(value, true);
            markup.IsTurnedOn = mode != MarkupMode.View;
            markup.Editor.Mode = mode;
            Pump(300);
            break;
        case "edit":
            MarkupItemEditor editor = controller.MarkupBar.ItemEditor ?? throw new InvalidOperationException("No markup popup is open");
            if (value == "!cancel" || value == "!delete")
            {
                editor.FindControl<Button>(value == "!cancel" ? "CancelButton" : "DeleteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                string[] fields = value.Split('|');
                editor.FindControl<TextBox>("LabelBox")!.Text = fields[0];
                if (fields.Length > 1 && fields[1].Length > 0) editor.Item.Color = fields[1];
                if (fields.Length > 2 && editor.Item is MapPin editedPin) editedPin.Icon = fields[2].Length > 0 ? fields[2] : null;
                if (fields.Length > 3) editor.FindControl<TextBox>("NoteBox")!.Text = fields[3];
                editor.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Pump(300);
            break;
        case "keys":
            if (value.Length == 0)
            {
                ShortcutsPanel panel = controller.ShortcutsPanel;
                Console.WriteLine($"  key list {(panel.IsOpen ? "open" : "closed")}; editing {panel.Editing?.ToString() ?? "-"}; message: {panel.Message ?? "-"}");
                Console.WriteLine($"  kept: {(controller.Settings.Keys is { } kept ? string.Join(", ", kept.Select(k => $"{k.Key}={k.Value}")) : "(none)")}");
                string ToolTipOf(string start) => controller.ToolbarWidget.ToolButtonSegments.SelectMany(segment => segment.Tools).First(t => t.ToolTip!.StartsWith(start)).ToolTip!;
                Console.WriteLine($"  tooltips: {ToolTipOf("Grid")} | {ToolTipOf("Cardinal")} | {ToolTipOf("Keyboard")} | pin: {ToolTip.GetTip(controller.MarkupBar.FindControl<Button>("PinButton")!)} | hide: {ToolTip.GetTip(controller.MarkupBar.FindControl<Button>("HideButton")!)}");
                foreach (ShortcutHelp line in controller.Keys.Map.Help(false).SelectMany(g => g.Lines).Where(l => l.Action is not null && controller.Keys.IsChanged(l.Action.Value)))
                {
                    Console.WriteLine($"  changed: {line.Keys} - {line.Description}");
                }
            }
            else
            {
                Control target = main.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.IsEffectivelyVisible && c.Tag as string == value)
                    ?? throw new InvalidOperationException($"Nothing in the key list is tagged {value}");
                // A corner, for the dimmed map and the card, clear of what's on them; the middle otherwise
                Point local = value is "outside" or "card" ? new Point(5, 5) : new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
                Point onMain = target.TranslatePoint(local, main) ?? throw new InvalidOperationException($"{value} isn't on the main window");
                main.MouseMove(onMain, RawInputModifiers.None);
                main.MouseDown(onMain, MouseButton.Left, RawInputModifiers.None);
                main.MouseUp(onMain, MouseButton.Left, RawInputModifiers.None);
                Pump(250);
            }
            break;
        case "recent":
            if (value.Length == 0)
            {
                controller.ToolbarWidget.RecentTool.IsTurnedOn = true;
                Pump(500);
                foreach (Button row in RecentRows()) Console.WriteLine($"  {string.Join(" | ", ((StackPanel)row.Content!).Children.OfType<TextBlock>().Select(t => t.Text))}  [{ToolTip.GetTip(row)}]");
                if (!RecentRows().Any()) Console.WriteLine("  (no rows)");
                Console.WriteLine($"  {main.GetVisualDescendants().OfType<RecentWorldsMenu>().Count()} menu(s) open");
            }
            else
            {
                RecentRow(value).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(1500);
            }
            break;
        case "recent-x":
            Button pressed = value == "!clear"
                ? main.GetVisualDescendants().OfType<RecentWorldsMenu>().Single().FindControl<Button>("ClearButton")!
                : ((Grid)RecentRow(value).Parent!).Children.OfType<Button>().Last();
            pressed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(300);
            break;
        case "dragover":
        case "drop":
            DataTransfer dragged = new();
            foreach (string path in value.Split('|'))
            {
                DataTransferItem item = new();
                item.SetFile(LocalItem(main, Path.GetFullPath(path)));
                dragged.Add(item);
            }
            Point middle = new(main.Bounds.Width / 2, main.Bounds.Height / 2);
            main.DragDrop(middle, Avalonia.Input.Raw.RawDragEventType.DragEnter, dragged, DragDropEffects.Copy, RawInputModifiers.None);
            main.DragDrop(middle, Avalonia.Input.Raw.RawDragEventType.DragOver, dragged, DragDropEffects.Copy, RawInputModifiers.None);
            Pump(300);
            Console.WriteLine($"  overlay: {controller.DropOverlay.Text ?? "(hidden)"}");
            if (parts[0] == "drop")
            {
                main.DragDrop(middle, Avalonia.Input.Raw.RawDragEventType.Drop, dragged, DragDropEffects.Copy, RawInputModifiers.None);
                Pump(2500);
                Console.WriteLine($"  after the drop: {controller.DropOverlay.Text ?? "(overlay hidden)"}");
            }
            heldDrag = dragged;
            break;
        case "dragleave":
            main.DragDrop(new Point(-5, -5), Avalonia.Input.Raw.RawDragEventType.DragLeave, heldDrag ?? new DataTransfer(), DragDropEffects.Copy, RawInputModifiers.None);
            Pump(300);
            Console.WriteLine($"  overlay: {controller.DropOverlay.Text ?? "(hidden)"}");
            break;
        case "find":
        case "find-start":
            {
                string[] fields = value.Split('|');
                FindWindow window = OpenFind();
                foreach (FindChip chip in window.Panel.Chips.ToList()) window.Panel.Remove(chip);
                foreach (string name in fields[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    FindTarget target = FindGroups.Find(name) ?? FindTarget.Of(name);
                    string? refused = window.Panel.Add(target);
                    if (refused is not null) Console.WriteLine($"  refused {name}: {refused}");
                }
                window.FindControl<TextBox>("FromBox")!.Text = fields.Length > 1 ? fields[1] : "";
                window.FindControl<TextBox>("ToBox")!.Text = fields.Length > 2 ? fields[2] : "";
                var findWatch = System.Diagnostics.Stopwatch.StartNew();
                window.FindControl<Button>("FindButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(100);
                if (parts[0] == "find-start") break;
                DateTime giveUp = DateTime.Now.AddSeconds(180);
                while (controller.ToolbarWidget.FindTool.Searching && DateTime.Now < giveUp) Pump(50);
                Pump(300);
                Console.WriteLine($"  searched in {findWatch.ElapsedMilliseconds} ms");
                PrintFind(window);
                break;
            }
        case "find-type":
            {
                FindWindow window = OpenFind();
                window.FindControl<TextBox>("SearchBox")!.Text = value;
                Pump(200);
                Console.WriteLine($"  {window.Panel.Blocks.Count} blocks: {string.Join(" | ", window.Panel.Blocks.Take(6).Select(b => $"{b.Title} ({b.Id})"))}");
                break;
            }
        case "find-pick":
            {
                FindWindow window = OpenFind();
                window.FindControl<ListBox>("ResultList")!.SelectedIndex = int.Parse(value);
                Pump(400);
                var picked = controller.ToolbarWidget.FindTool.Picked;
                Console.WriteLine($"  picked {(picked is null ? "nothing" : controller.ToolbarWidget.FindTool.Label(picked) + $" at {picked.X}, {picked.Y}, {picked.Z}")}; map centre {controller.Domain.CurrentWorld?.CurrentDimension.Scaling.CenterPoint}");
                break;
            }
        case "find-pin":
        case "find-stop":
            {
                FindWindow window = OpenFind();
                window.FindControl<Button>(parts[0] == "find-pin" ? "PinButton" : "StopButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(800);
                PrintFind(window);
                break;
            }
        case "find-state":
            PrintFind(OpenFind());
            break;
        case "marks":
            MarkupTool marks = controller.ToolbarWidget.MarkupTool;
            foreach (MapPin pin in marks.Editor.Document.Pins) Console.WriteLine($"  pin {pin.Label} at {pin.X}, {pin.Z} in {pin.Dimension}, {pin.Color} {pin.Icon ?? "dot"}{(pin.Note is null ? "" : $", note {pin.Note}")}");
            foreach (MapZone zone in marks.Editor.Document.Zones) Console.WriteLine($"  zone {zone.Label} in {zone.Dimension}, {zone.Color}: {string.Join(" ", zone.Points.Select(p => $"{p.X},{p.Z}"))} ({ZoneGeometry.Describe(zone.Points)})");
            Console.WriteLine($"  places: {string.Join(" | ", marks.Places().Select(MarkupTool.Title))}; mode {marks.Editor.Mode}, shown {marks.ShowMarkup}, file {marks.Store.FileFor(marks.Editor.Document.World ?? "?")}");
            break;
        default:
            throw new ArgumentException($"Unknown step {step}");
    }
}

return 0;

void Start(string? startupWorld)
{
    controller = new Controller(AppSettings.Load(settingsFile), startupWorld);
    main = controller.MainWindow;
    controller.InformationWidget.ColumnUpdate += (sender, column) => lastColumn = column;
    if (!File.Exists(settingsFile))
    {
        main.Width = 1250;
        main.Height = 800;
    }
    // Popups drawn into the window, so screenshots show them
    foreach (Popup popup in controller.MarkupBar.GetLogicalDescendants().OfType<Popup>()) popup.ShouldUseOverlayLayer = true;
    main.Show();
    Pump(1500);
}

// Runs the dispatcher's own loop in short frames, so DispatcherTimers fire as they would in the app (RunJobs alone
// runs queued work but not timers)
void Pump(int milliseconds)
{
    DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
    do
    {
        DispatcherFrame frame = new();
        DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(10));
        Dispatcher.UIThread.PushFrame(frame);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    while (DateTime.Now < end);
}

void Settle()
{
    int last = -1;
    DateTime stableSince = DateTime.Now, giveUp = DateTime.Now.AddSeconds(120);
    while (DateTime.Now < giveUp)
    {
        Pump(100);
        int count = controller.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0;
        if (count != last) { last = count; stableSince = DateTime.Now; }
        else if (DateTime.Now - stableSince > TimeSpan.FromSeconds(1.5)) break;
    }
    Console.WriteLine($"  {last} regions rendered");
}

void Shot(string name)
{
    Pump(200);
    List<Window> order = [main, .. windows.Where(window => window != main)];
    for (int i = 0; i < order.Count; i++)
    {
        string file = Path.Combine(output, $"{name}-{i}.png");
        order[i].CaptureRenderedFrame()?.Save(file);
        Console.WriteLine($"  {file} ({order[i].Title})");
    }
}

// The open find window, opening it as key F does if it isn't
FindWindow OpenFind()
{
    if (windows.OfType<FindWindow>().FirstOrDefault() is FindWindow open) return open;
    controller.ToolbarWidget.FindTool.IsTurnedOn = true;
    Pump(800);
    return windows.OfType<FindWindow>().FirstOrDefault() ?? throw new InvalidOperationException("Find blocks didn't open its window");
}

void PrintFind(FindWindow window)
{
    FindTool tool = controller.ToolbarWidget.FindTool;
    Console.WriteLine($"  status: {window.FindControl<TextBlock>("StatusText")!.Text}");
    if (window.FindControl<TextBlock>("CountsText")!.Text is { Length: > 0 } counts) Console.WriteLine($"  counts: {counts}");
    Console.WriteLine($"  chosen: {string.Join(", ", window.Panel.Chips.Select(c => $"{c.Label} {c.Color}"))}; {window.FindControl<TextBlock>("SortedText")!.Text}");
    foreach (FindResultRow row in window.Panel.Rows().Take(5)) Console.WriteLine($"  row: {row.Title} at {row.Place}, {row.Distance}");
    if (tool.Results is FindResults results)
    {
        FindFrame frame = FindFrame.Build(results, tool.View, controller.GraphicsCanvas.ActualWidth, controller.GraphicsCanvas.ActualHeight, tool.Picked);
        Console.WriteLine($"  on screen: {frame.Marks.Count} {(frame.Dots ? "dots" : "chunk squares")}{(frame.Ring is null ? "" : ", a ring")}; buttons: go {window.FindControl<Button>("GoButton")!.IsEnabled}, pin {window.FindControl<Button>("PinButton")!.IsEnabled}");
    }
}

IEnumerable<Button> RecentRows() => main.GetVisualDescendants().OfType<RecentWorldsMenu>().SelectMany(menu => menu.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("recent")));
Button RecentRow(string name) => RecentRows().FirstOrDefault(row => ((StackPanel)row.Content!).Children.OfType<TextBlock>().First().Text == name)
    ?? throw new InvalidOperationException($"The Recent menu has no row {name}");

void CloseOthers()
{
    foreach (Window window in windows.Where(window => window != main).ToList()) window.Close();
    Pump(300);
}

static string Describe(InspectedColumn? column)
{
    if (column is null) return "nothing";
    string text = $"{column.Block.Name} at Y {column.Y}, biome {column.Biome ?? "none"}";
    if (column.Depth > 0) text += $"; {column.Depth} deep over {column.Floor?.Name ?? "nothing"} at Y {column.FloorY}";
    return text;
}

static Point ParsePoint(string text)
{
    double[] values = text.Split(',').Select(double.Parse).ToArray();
    return new Point(values[0], values[1]);
}

// A file or folder on this machine, as the system's drag would hand one over: the platform's own storage item if it
// gives one, else Avalonia's plain local-file one (which user code can't make, its interfaces being closed)
static IStorageItem LocalItem(TopLevel top, string path)
{
    IStorageItem? item = Directory.Exists(path)
        ? top.StorageProvider.TryGetFolderFromPathAsync(path).GetAwaiter().GetResult()
        : top.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult();
    if (item is not null) return item;

    Type type = typeof(IStorageItem).Assembly.GetType(Directory.Exists(path) ? "Avalonia.Platform.Storage.FileIO.BclStorageFolder" : "Avalonia.Platform.Storage.FileIO.BclStorageFile")
        ?? throw new InvalidOperationException("No local storage item type in this Avalonia");
    object info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    return (IStorageItem)Activator.CreateInstance(type, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, [info], null)!;
}

class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
