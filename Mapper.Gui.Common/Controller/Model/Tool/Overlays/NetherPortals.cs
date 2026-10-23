using Mapper.Gui.Logic;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldEditor;
#if AVALONIA
using Avalonia.Threading;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    // The world's Nether portals and how they link, for the portal overlay of either GUI. Both the Overworld and the
    // Nether are searched in the background the first time the overlay asks, and again when Live redraws saved regions
    public class NetherPortals
    {
        public Scene Scene { get; }

        public PortalMap Map { get; private set; } = PortalMap.Empty;
        public bool Reading { get; private set; }

        // Raised on the UI thread when Map changes
        public event Action? Changed;

        private WorldDomain? _world;
        private bool _read;
        private bool _again;
        private CancellationTokenSource? _cancel;

        public NetherPortals(Scene scene, LiveReloadTool live)
        {
            Scene = scene;
            Scene.WorldChanged += (old, current) => Forget();
            live.Refreshed += (dimension, regions) =>
            {
                if (!_read) return;
                if (Reading) _again = true;
                else Read();
            };
        }

        // The overlay is on: reads the world's portals unless they've been read
        public void Want()
        {
            if (_read || Reading || Scene.Domain.CurrentWorld is null) return;
            Read();
        }

        private void Read()
        {
            if (Scene.Domain.CurrentWorld is not WorldDomain world) return;

            _cancel?.Cancel();
            CancellationTokenSource cancel = _cancel = new CancellationTokenSource();
            _world = world;
            _read = true;
            Reading = true;
            _again = false;

            // The dimensions already open are read through the stores the map uses
            SceneInfo? overworld = world.GetDimension(Dimension.Overworld)?.Scene.SceneParameter;
            SceneInfo? nether = world.GetDimension(Dimension.Nether)?.Scene.SceneParameter;
            Level level = world.Level;

            _ = Task.Run(() =>
            {
                PortalMap? map = null;
                try
                {
                    map = new PortalMap(Search(overworld, level, Dimension.Overworld, cancel.Token), Search(nether, level, Dimension.Nether, cancel.Token));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Nether portals: {e.Message}");
                }

                Post(() =>
                {
                    if (cancel.IsCancellationRequested || _world != world) return;
                    Reading = false;
                    if (map is not null) Map = map;
                    Changed?.Invoke();
                    if (_again) Read();
                });
            });
        }

        // A dimension the world doesn't have, or can't be read, has no portals
        private static List<BlockMatch> Search(SceneInfo? scene, Level level, Dimension dimension, CancellationToken cancellation)
        {
            try
            {
                return PortalMap.Search(scene ?? new SceneInfo(level, dimension), cancellation);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Console.WriteLine($"Nether portals in the {dimension.Name}: {e.Message}");
                return [];
            }
        }

        private void Forget()
        {
            _cancel?.Cancel();
            _cancel = null;
            _world = null;
            _read = false;
            Reading = false;
            Map = PortalMap.Empty;
            Changed?.Invoke();
        }

        private static void Post(Action action)
        {
#if AVALONIA
            Dispatcher.UIThread.Post(action);
#else
            Application.Current?.Dispatcher.BeginInvoke(action);
#endif
        }
    }
}
