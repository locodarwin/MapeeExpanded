using Mapper.Gui.Controller;
using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mapper.Gui
{
    // Mapee.exe <world> --smoke-test=<folder>: opens the world, presses every toolbar button, opens every menu and
    // window they lead to, runs a search and narrows the window, saving a picture of every window and menu on show
    // after each step, then exits: 0 if every step did what it should, 1 if not, 2 if it ran out of time.
    // report.txt in the folder says which steps passed. It's how CI checks the Windows GUI, which nothing else runs
    // before Shawn does. It runs as the app does, from the published exe, on the dispatcher; steps await rather than
    // block, so a modal window (Export, the world browser) can be looked at and closed while it's open
    public class SmokeTest
    {
        private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

        private readonly string _folder;
        private readonly string? _world;
        private readonly List<string> _report = [];
        private readonly List<string> _errors = [];
        private Controller.Controller _controller = null!;
        private int _shots;
        private int _failed;
        private Timer? _watchdog;

        public SmokeTest(string folder, string? world)
        {
            _folder = Path.GetFullPath(folder);
            _world = world;
        }

        public void Start(Application app)
        {
            Directory.CreateDirectory(_folder);
            foreach (string old in Directory.GetFiles(_folder, "*.png")) File.Delete(old);

            app.DispatcherUnhandledException += (sender, e) =>
            {
                _errors.Add($"{e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");
                e.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (sender, e) => _errors.Add($"Unobserved task: {e.Exception.Message}");

            _watchdog = new Timer(_ =>
            {
                Line("FAIL", "finish within " + Limit.TotalMinutes + " minutes", "still running");
                // What went wrong on the way, such as the app failing to start, which a hang never gets to report
                foreach (string error in _errors.ToList()) Line("ERROR", error, null);
                Finish(2);
            }, null, Limit, Timeout.InfiniteTimeSpan);

            _controller = new Controller.Controller(AppSettings.Load(Path.Combine(_folder, "Settings.json")), _world);
            MapViewer main = _controller.MainWindow;
            main.Width = 1400;
            main.Height = 900;
            main.Show();

            main.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await Run();
                }
                catch (Exception e)
                {
                    Line("FAIL", "the smoke test itself", $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
                Finish(_failed > 0 || _errors.Count > 0 ? 1 : 0);
            });
        }

        private async Task Run()
        {
            ToolbarWidget toolbar = _controller.ToolbarWidget;

            await Until(() => (_controller.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0) > 0, 60);
            await Settle();
            Check("open the world and draw it", (_controller.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0) > 0,
                $"{_controller.Domain.CurrentWorld?.Level.WorldName ?? "no world"}, {_controller.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0} regions drawn");
            Check("toolbar labels in a wide window", Labelled(), "labels hidden at 1400 px");
            await Shoot("start");

            await Press("Open", () => Find<RecentWorldsMenu>() is not null, async () =>
            {
                Button browse = Find<RecentWorldsMenu>()!.FindName("BrowseButton") as Button ?? throw new InvalidOperationException("no Browse row");
                await Click(browse);
                Check("Open > Browse for a world", Window<WorldBrowser>() is not null, "the world browser didn't open");
                await Shoot("open-browse");
            });
            await Press("Go to", () => Window<GoToWindow>() is not null);
            await Press("Find", () => Window<FindWindow>() is not null, async () =>
            {
                FindTool find = toolbar.FindTool;
                find.Start(new BlockQuery([FindGroups.Find("diamonds")!, FindGroups.Find("spawners")!]));
                await Until(() => !find.Searching, 60);
                Check("Find diamonds and spawners", (find.Results?.Total ?? 0) > 0, $"{find.Results?.Total ?? 0} found");
                await Shoot("find-results");

                // The made-up world has one sign; ticked, it's shown with the world's markup
                FindWindow window = Window<FindWindow>()!;
                await Click((Button)window.FindName("SignsButton"));
                await Until(() => toolbar.Signs.Current is not null && !toolbar.Signs.Reading && window.Signs?.Listed.Count > 0, 60);
                SignRow? row = window.Signs?.Listed.FirstOrDefault();
                Check("Find > Signs", row?.Title == "Welcome to" && row.More == "the smoke test" && window.Signs!.Listed.Count == 1,
                    string.Join(", ", window.Signs?.Listed.Select(r => $"{r.Title} {r.More} at {r.Place}") ?? []));
                if (row is not null) row.Shown = true;
                await Wait(300);
                MarkupDocument markup = toolbar.MarkupTool.Editor.Document;
                Check("Find > Signs: show one on the map", markup.Signs.Count == 1 && toolbar.MarkupTool.ShowMarkup,
                    $"{markup.Signs.Count} shown, markup {(toolbar.MarkupTool.ShowMarkup ? "shown" : "hidden")}");
                await Shoot("find-signs");
                if (row is not null) row.Shown = false;
            });
            await Press("Mark", () => toolbar.MarkupTool.IsTurnedOn && _controller.MarkupBar.IsVisible, turnOff: true);
            await Press("Measure", () => toolbar.MeasureLengthTool.IsTurnedOn, Measure, turnOff: true);
            await Press("Overlays", () => Find<OverlaysMenu>() is not null, async () =>
            {
                foreach (Button row in Descendants<Button>(Find<OverlaysMenu>()!)) await Click(row);
                int on = toolbar.OverlaysTool.Items.Count(i => i.Tool.IsTurnedOn);
                int all = toolbar.OverlaysTool.Items.Count;
                Check($"Overlays: tick all {all}", on == all && Badge("Overlays") == all, $"{on} on, badge {Badge("Overlays")}");

                // The made-up world is open ground, dark at night; it has no portals and no Nether, and one village
                SpawnTool spawn = (SpawnTool)toolbar.Overlays["spawn_spots"];
                PortalTool portals = (PortalTool)toolbar.Overlays["portals"];
                StructureTool structures = (StructureTool)toolbar.Overlays["structures"];
                await Until(() => spawn.Spots.Regions.Any() && !portals.Portals.Reading && structures.Structures.Current.Any(), 60);
                int spots = spawn.Spots.Regions.Sum(r => r.Count);
                Check("Overlays: where mobs can spawn", spots > 0, $"{spots:N0} blocks in {spawn.Spots.Regions.Count()} regions");
                Check("Overlays: Nether portals", !portals.Portals.Reading && !portals.Portals.Map.Overworld.Any(), $"{portals.Portals.Map.Overworld.Count} in the Overworld");
                Check("Overlays: structures", structures.Structures.Current.Select(s => s.Name).SequenceEqual(["Plains village"]),
                    string.Join(", ", structures.Structures.Current.Select(s => $"{s.Name} at {s}")));
                await Shoot("overlays-on");
                foreach (OverlayItem item in toolbar.OverlaysTool.Items) item.Tool.IsTurnedOn = false;
            });
            bool night = toolbar.NightTool.IsTurnedOn, live = toolbar.LiveTool.IsTurnedOn;
            await Press("Night", () => toolbar.NightTool.IsTurnedOn != night, turnOff: true);
            await Press("Live", () => toolbar.LiveTool.IsTurnedOn != live, turnOff: true);
            await Press("Filter", () => Window<BlockFilterControl>() is not null);
            await Press("Render", () => Window<RenderSettingsControl>() is not null);
            await Press("Export", () => Window<SaveAsImageControl>() is not null);
            await Press("Keys", () => _controller.ShortcutsPanel.IsOpen, after: () =>
            {
                _controller.ShortcutsPanel.Close();
                return Task.CompletedTask;
            });

            _controller.MainWindow.Width = 900;
            await Wait(800);
            Check("icons only in a 900 px window", !Labelled(), "labels still shown");
            await Shoot("narrow");
            _controller.MainWindow.Width = 1400;
            await Wait(800);
            toolbar.ShowLabels = false;
            _controller.Toolbar.Fit();
            await Wait(500);
            Check("labels turned off by the toolbar's menu", !Labelled(), "labels still shown");
            await Shoot("labels-off");
            toolbar.ShowLabels = true;
            _controller.Toolbar.Fit();
            await Wait(500);
            Check("labels back on", Labelled(), "labels still hidden");

            Check("no errors on the way", _errors.Count == 0, string.Join("\n", _errors));
        }

        // Drags the real mouse across the map with Measure on and checks it measured between the blocks under the
        // cursor, which it didn't on a monitor scaled unlike the main one. Skipped if this machine takes no mouse input
        private async Task Measure()
        {
            Logic.MeasureLengthTool tool = _controller.ToolbarWidget.MeasureLengthTool;
            FrameworkElement canvas = _controller.GraphicsCanvas;
            Point from = new(canvas.ActualWidth * 0.3, canvas.ActualHeight * 0.4), to = new(canvas.ActualWidth * 0.6, canvas.ActualHeight * 0.7);

            async Task MoveTo(Point point)
            {
                Point screen = canvas.PointToScreen(point);
                SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
                mouse_event(MouseMove, 0, 0, 0, IntPtr.Zero);
                await Wait(200);
            }
            int hooked = 0;
            void Count(object? sender, Point point) => hooked++;
            MouseHook.MouseMove += Count;
            await MoveTo(from);
            mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero);
            await Wait(200);
            await MoveTo(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            await MoveTo(to);
            mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero);
            await Wait(300);
            MouseHook.MouseMove -= Count;

            if (tool.Measured is not var (first, second))
            {
                GetCursorPos(out CursorPoint cursor);
                Point want = canvas.PointToScreen(to);
                Line("SKIP", "measure a drag across the map", $"no measurement: the hook saw {hooked} moves, the cursor is at {cursor.X}, {cursor.Y} " +
                    $"(asked for {want.X:0}, {want.Y:0}), the map {(canvas.IsMouseOver ? "is" : "isn't")} under it");
                return;
            }
            XzPoint wantFirst = _controller.ImplementedScene.Map.TransformPointOnScreenToXz(from), wantSecond = _controller.ImplementedScene.Map.TransformPointOnScreenToXz(to);
            bool Near(XzPoint a, XzPoint b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Z - b.Z) <= 1;
            Check("measure a drag across the map", Near(first, wantFirst) && Near(second, wantSecond),
                $"measured {first.X}, {first.Z} to {second.X}, {second.Z}; the cursor was over {wantFirst.X:0}, {wantFirst.Z:0} to {wantSecond.X:0}, {wantSecond.Z:0}");
            await Shoot("measure-drag");
        }

        private const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        private struct CursorPoint
        {
            public int X, Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

        // Presses a toolbar button by its label, checks it did what it should, runs anything more on what it opened,
        // pictures it, and puts things back: windows and menus it opened closed, a tool it turned on turned off
        private async Task Press(string name, Func<bool> worked, Func<Task>? after = null, bool turnOff = false)
        {
            IToolButton? button = _controller.ToolbarWidget.ToolButtonSegments.SelectMany(s => s.Tools).FirstOrDefault(b => b.Name == name);
            if (button?.Button is null)
            {
                Check($"press {name}", false, "no such button");
                return;
            }

            HashSet<PresentationSource> before = [.. PresentationSource.CurrentSources.OfType<PresentationSource>()];
            await Click(button.Button);
            await Until(worked, 10);
            Check($"press {name}", worked(), "nothing opened or changed");
            await Shoot(name.Replace(' ', '-').ToLowerInvariant());

            if (after is not null) await after();

            if (turnOff && button.Tool.IsTurnedOn) await Click(button.Button);
            foreach (PresentationSource source in PresentationSource.CurrentSources.OfType<PresentationSource>().Where(s => !before.Contains(s)).ToList())
            {
                if (source.RootVisual is Window window) window.Close();
                else if (source.RootVisual is not null)
                {
                    foreach (FrameworkElement element in Descendants<FrameworkElement>(source.RootVisual))
                    {
                        if (element.Parent is Popup popup) popup.IsOpen = false;
                    }
                }
            }
            await Wait(500);
        }

        // Raised from the dispatcher's queue, not from here, so a button that opens a modal window doesn't hold up the
        // test: the window's own loop runs the rest of it
        private async Task Click(ButtonBase button)
        {
            _ = button.Dispatcher.BeginInvoke(() => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)));
            await Wait(800);
        }

        private bool Labelled() =>
            _controller.ToolbarWidget.ToolButtonSegments.SelectMany(s => s.Tools).All(b => b.Button is { ActualHeight: > 40 });

        private int Badge(string name)
        {
            IToolButton? button = _controller.ToolbarWidget.ToolButtonSegments.SelectMany(s => s.Tools).FirstOrDefault(b => b.Name == name);
            return button?.Badge ?? -1;
        }

        private static T? Window<T>() where T : Window => Application.Current.Windows.OfType<T>().FirstOrDefault(w => w.IsVisible);

        // Something on show anywhere, the main window or a menu's own pop-up
        private static T? Find<T>() where T : DependencyObject =>
            PresentationSource.CurrentSources.OfType<PresentationSource>().Select(s => s.RootVisual).Where(v => v is not null)
                .SelectMany(v => Descendants<T>(v!)).FirstOrDefault();

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (T inner in Descendants<T>(child)) yield return inner;
            }
        }

        // Every window and pop-up on show, each into its own picture
        private async Task Shoot(string name)
        {
            await Wait(300);
            int index = 0;
            foreach (PresentationSource source in PresentationSource.CurrentSources.OfType<PresentationSource>().ToList())
            {
                if (source.RootVisual is not FrameworkElement root || !root.IsVisible || root.ActualWidth < 1 || root.ActualHeight < 1) continue;
                try
                {
                    RenderTargetBitmap bitmap = new((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    string kind = root is Window window ? window.GetType().Name : "popup";
                    using FileStream file = File.Create(Path.Combine(_folder, $"{++_shots:00}-{name}-{index++}-{kind}.png"));
                    encoder.Save(file);
                }
                catch (Exception e)
                {
                    _errors.Add($"Picture of {name}: {e.Message}");
                }
            }
        }

        private void Check(string step, bool passed, string why)
        {
            if (passed) Line("PASS", step, null);
            else
            {
                _failed++;
                Line("FAIL", step, why);
            }
        }

        private void Line(string result, string step, string? why)
        {
            lock (_report) _report.Add(why is null ? $"{result}  {step}" : $"{result}  {step}: {why}");
        }

        private void Finish(int code)
        {
            lock (_report)
            {
                _report.Add(code == 0 ? "All passed" : $"Exit code {code}");
                File.WriteAllLines(Path.Combine(_folder, "report.txt"), _report);
            }
            Environment.Exit(code);
        }

        private static Task Wait(int milliseconds) => Task.Delay(milliseconds);

        private static async Task Until(Func<bool> condition, int seconds)
        {
            DateTime end = DateTime.Now.AddSeconds(seconds);
            while (!condition() && DateTime.Now < end) await Task.Delay(100);
        }

        // Until no new region has been drawn for a second and a half
        private async Task Settle()
        {
            int last = -1;
            DateTime stableSince = DateTime.Now, end = DateTime.Now.AddSeconds(90);
            while (DateTime.Now < end)
            {
                await Task.Delay(100);
                int count = _controller.Domain.CurrentWorld?.CurrentDimension.Scene.RenderedRegions.Count ?? 0;
                if (count != last)
                {
                    last = count;
                    stableSince = DateTime.Now;
                }
                else if (DateTime.Now - stableSince > TimeSpan.FromSeconds(1.5)) break;
            }
        }
    }
}
