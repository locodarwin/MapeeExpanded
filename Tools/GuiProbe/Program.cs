// Runs the Avalonia GUI on Avalonia's headless platform with Skia drawing and acts out a list of steps, saving
// screenshots of every open window along the way.
//   GuiProbe <output folder> <step>...
// Steps:
//   load:<world folder or .mcworld>   open a world, as the world browser would
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
//   drag:<x1>,<y1>,<x2>,<y2>          left-drag across the main window
//   save:<screenshot|full>:<file>     export the map as the Export as image window would
//   close                             close every window but the main one
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

Controller controller = new();
MapViewer main = controller.MainWindow;
main.Width = 1250;
main.Height = 800;
main.Show();
Pump(1500);

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
                .First(tool => string.Equals(tool.ToolTip, value, StringComparison.OrdinalIgnoreCase) || string.Equals(tool.Name?.Trim(), value, StringComparison.OrdinalIgnoreCase));
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
            if (save[0] == "screenshot") saver.SaveAsScreenshot(save[1]);
            else saver.SaveAsFullResolution(save[1], saver.DefaultFullResArgs);
            Pump(200);
            Console.WriteLine($"  saved {save[1]} ({new FileInfo(save[1]).Length:N0} bytes)");
            break;
        case "close":
            CloseOthers();
            break;
        default:
            throw new ArgumentException($"Unknown step {step}");
    }
}

return 0;

void Pump(int milliseconds)
{
    DateTime end = DateTime.Now.AddMilliseconds(milliseconds);
    do
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(10);
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

void CloseOthers()
{
    foreach (Window window in windows.Where(window => window != main).ToList()) window.Close();
    Pump(300);
}

static Point ParsePoint(string text)
{
    double[] values = text.Split(',').Select(double.Parse).ToArray();
    return new Point(values[0], values[1]);
}
