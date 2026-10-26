using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldEditor;
#if AVALONIA
using Avalonia;
using Avalonia.Threading;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    public enum FindHalf { Blocks, Signs, Pets, Biomes }

    // The Find blocks button: opens the find window, searches the whole dimension on show in the background and
    // marks what it finds on the map (FindPainter). While Live is on, regions the game saves are searched again.
    // Switching dimension or world clears the results; closing the window takes the marks away. The window's other
    // halves list the signs (WorldSigns) and named mobs (WorldPets), whose ticks stay with the world's markup, and the
    // biomes (WorldBiomes), shaded on the map while their list is open
    public class FindTool : ToggleableTool
    {
        public Scene Scene { get; }
        public CanvasControl Canvas { get; }
        public MarkupTool Markup { get; }
        public SceneMarkupView View { get; }
        public WorldSigns? Signs { get; init; }
        public WorldPets? Pets { get; init; }
        public WorldBiomes? Biomes { get; init; }

        // The window opens on the half it was on when it was closed
        public FindHalf Half { get; set; }

        // The search on show, or null; read by the painter on the UI thread
        public FindResults? Results { get; private set; }
        // What was looked for last, kept after the results are cleared so the window opens with it chosen
        public BlockQuery? LastQuery { get; private set; }
        public DimensionDomain? Dimension { get; private set; }

        // Veins nearest the place Sorted names first
        public IReadOnlyList<FoundCluster> Clusters { get; private set; } = [];
        public XzPoint Sorted { get; private set; }

        public FoundCluster? Picked { get; private set; }

        public bool Searching { get; private set; }
        public int RegionsDone { get; private set; }
        public int RegionsTotal { get; private set; }
        // The search stopped at BlockFinder.MaxMatches
        public bool Stopped { get; private set; }
        // Stopped with the Stop button; what was found so far stays
        public bool Halted { get; private set; }
        public string? Problem { get; private set; }

        // Raised on the UI thread whenever any of the above changes
        public event EventHandler? Changed;

        private readonly IRenderInvoker _renderer;
        private FindWindow? _window;
        private CancellationTokenSource? _cancel;
        private int _generation;

        public FindTool(Scene scene, CanvasControl canvas, MarkupTool markup, LiveReloadTool live, IRenderInvoker renderer)
        {
            Scene = scene;
            Canvas = canvas;
            Markup = markup;
            View = new SceneMarkupView(scene);
            _renderer = renderer;

            Enabled = false;
            Scene.WorldChanged += (old, current) =>
            {
                Enabled = true;
                Clear();
            };
            Scene.DimensionChanged += (old, current) => Clear();
            live.Refreshed += SearchAgain;

            OnTurnedOn += isTurnedOn =>
            {
                if (!isTurnedOn) return;
                IsTurnedOn = false;
                Open();
            };
        }

        private void Open()
        {
            if (Scene.Domain.CurrentWorld is null) return;
            if (_window is not null)
            {
                _window.Activate();
                return;
            }

            _window = new FindWindow(this);
            _window.Closed += (sender, e) =>
            {
                _window = null;
                Clear();
            };
            AppWindows.ShowTool(_window);
        }

        // Starts a search of the dimension on show, replacing whatever was found before
        public void Start(BlockQuery query)
        {
            if (Scene.Domain.CurrentWorld?.CurrentDimension is not DimensionDomain dimension || query.Targets.Count == 0) return;
            Cancel();

            int generation = ++_generation;
            CancellationTokenSource cancel = _cancel = new CancellationTokenSource();
            FindResults results = new(query);

            Results = results;
            LastQuery = query;
            Dimension = dimension;
            Clusters = [];
            Picked = null;
            Searching = true;
            Stopped = false;
            Halted = false;
            Problem = null;
            RegionsDone = RegionsTotal = 0;
            Raise();

            SceneInfo scene = dimension.Scene.SceneParameter;
            DateTime lastShown = DateTime.MinValue;

            _ = Task.Run(() =>
            {
                try
                {
                    // A Bedrock world still being indexed gives its regions once it's done
                    List<Coords> regions = [.. scene.Regions];
                    Post(generation, () => RegionsTotal = regions.Count);

                    bool complete = new BlockFinder(query).Search(scene.RegionStore, regions, (region, matches) =>
                    {
                        results.Set(region, matches);
                        int done = results.Regions;
                        // Shown a few times a second as it goes, so the map fills in
                        if (DateTime.UtcNow - lastShown > TimeSpan.FromMilliseconds(300))
                        {
                            lastShown = DateTime.UtcNow;
                            Post(generation, () => RegionsDone = done);
                        }
                    }, cancel.Token);

                    IReadOnlyList<FoundCluster> clusters = results.Clusters;
                    Post(generation, () =>
                    {
                        Searching = false;
                        Stopped = !complete;
                        RegionsDone = RegionsTotal;
                        SortClusters(clusters);
                    });
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Find blocks: {e}");
                    Post(generation, () =>
                    {
                        Searching = false;
                        Problem = e.Message;
                    });
                }
            });
        }

        // The Stop button: the search ends where it is, keeping what it found
        public void Stop()
        {
            if (!Searching || Results is not FindResults results) return;
            Cancel();
            Halted = true;
            RegionsDone = results.Regions;
            Raise();

            int generation = _generation;
            _ = Task.Run(() =>
            {
                IReadOnlyList<FoundCluster> clusters = results.Clusters;
                Post(generation, () => SortClusters(clusters));
            });
        }

        public void Cancel()
        {
            _cancel?.Cancel();
            _cancel = null;
            if (Searching)
            {
                Searching = false;
                Raise();
            }
        }

        // Forgets the search and takes its marks off the map
        public void Clear()
        {
            Cancel();
            _generation++;
            bool had = Results is not null;
            Results = null;
            Dimension = null;
            Clusters = [];
            Picked = null;
            Stopped = false;
            Halted = false;
            Problem = null;
            Raise();
            if (had) _renderer.Render();
        }

        // Lists the veins again, nearest the middle of the map first
        public void SortFromHere()
        {
            if (Results is null || Searching) return;
            SortClusters(Clusters);
        }

        private void SortClusters(IEnumerable<FoundCluster> clusters)
        {
            Sorted = Scene.Map.TransformPointOnScreenToXz(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
            Clusters = FindResults.ByDistance(clusters, Sorted.X, Sorted.Z);
        }

        // Centres the map on a vein and rings it
        public void GoTo(FoundCluster? cluster)
        {
            Picked = cluster;
            if (cluster is not null) Scene.Map.SetCenterPoint(new XzPoint(cluster.X + 0.5, cluster.Z + 0.5));
            Raise();
            _renderer.Render();
        }

        // Centres the map on a sign
        public void GoTo(Sign sign) => Scene.Map.SetCenterPoint(new XzPoint(sign.X + 0.5, sign.Z + 0.5));

        // Centres the map on where a mob was last saved
        public void GoTo(Pet pet) => Scene.Map.SetCenterPoint(new XzPoint(pet.X, pet.Z));

        // Centres the map on a block, such as a biome patch's nearest part
        public void GoTo(int x, int z) => Scene.Map.SetCenterPoint(new XzPoint(x + 0.5, z + 0.5));

        public bool CanPin => Markup.Enabled;

        // A pin on the vein, labelled with what and how deep, in the colour it's marked in
        public void Pin(FoundCluster cluster)
        {
            if (Results is null || !Markup.Enabled) return;

            MapPin pin = new()
            {
                X = cluster.X,
                Z = cluster.Z,
                Dimension = Markup.View.Dimension,
                Label = Label(cluster),
                Color = FindColors.PinColor(cluster.Target),
                Icon = "star"
            };
            Markup.ShowMarkup = true;
            Markup.Editor.Import(new MarkupDocument() { Pins = [pin] });
        }

        // "Diamonds ×8, Y -58 to -56"
        public string Label(FoundCluster cluster)
        {
            string what = Results is FindResults results && cluster.Target < results.Query.Targets.Count ? results.Query.Targets[cluster.Target].Label : "Found";
            string count = cluster.Count > 1 ? $" ×{cluster.Count}" : "";
            string height = cluster.MinY == cluster.MaxY ? $"Y {cluster.Y}" : $"Y {cluster.MinY} to {cluster.MaxY}";
            return $"{what}{count}, {height}";
        }

        // Live reload drew these regions again; what they hold now replaces what was found there before
        private void SearchAgain(DimensionDomain dimension, IReadOnlyCollection<Coords> regions)
        {
            FindResults? results = Results;
            if (results is null || Searching || dimension != Dimension) return;
            int generation = _generation;

            try
            {
                new BlockFinder(results.Query).Search(dimension.Scene.SceneParameter.RegionStore, [.. regions], results.Set);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Find blocks, searching saved regions again: {e.Message}");
                return;
            }

            IReadOnlyList<FoundCluster> clusters = results.Clusters;
            Post(generation, () =>
            {
                Clusters = FindResults.ByDistance(clusters, Sorted.X, Sorted.Z);
                if (Picked is not null && !clusters.Contains(Picked)) Picked = null;
            });
        }

        // Runs on the UI thread, unless the search it belongs to has been replaced since
        private void Post(int generation, Action action)
        {
            void Run()
            {
                if (generation != _generation) return;
                action();
                Raise();
                _renderer.Render();
            }
#if AVALONIA
            Dispatcher.UIThread.Post(Run);
#else
            Application.Current?.Dispatcher.BeginInvoke(Run);
#endif
        }

        private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
