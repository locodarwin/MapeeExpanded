using Mapper.Gui.Logic;
using System;
using System.Linq;
using System.Threading.Tasks;
using WorldEditor;
#if AVALONIA
using Avalonia;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    // Opens worlds for the world browser and the command line, and keeps the list of recent worlds in the
    // settings with where the map was left in each, so reopening a world returns to the same place
    public class WorldOpener
    {
        public Scene Scene { get; }
        public AppSettings Settings { get; }
        public DimensionWidget? DimensionWidget { get; set; }

        // A world named on the command line, opened in place of the browser at startup
        public string? StartupWorld { get; set; }

        // What the current world was opened from: its folder, or the .mcworld file
        private string? _currentSource;

        public WorldOpener(Scene scene, AppSettings settings)
        {
            Scene = scene;
            Settings = settings;
        }

        public void Open(string source, Level level)
        {
            RememberView();

            Scene.SetWorld(level);
            _currentSource = source;

            RestoreView(Settings.Remember(source));
            Settings.Save();
        }

        // Reads the world off the UI thread, since a .mcworld may need extracting first
        public async Task<bool> TryOpen(string path)
        {
            try
            {
                Level? level = await Task.Run(() => WorldLocation.Read(path));
                if (level is null) return false;

                Open(path, level);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Couldn't open {path}: {e.Message}");
                return false;
            }
        }

        public void RememberView()
        {
            if (_currentSource is null || Scene.Domain.CurrentWorld is null) return;

            RecentWorld? recent = Settings.FindRecent(_currentSource);
            if (recent is null) return;

            DimensionDomain dimension = Scene.Domain.CurrentWorld.CurrentDimension;
            recent.Dimension = dimension.Dimension.Namespace;
            recent.X = dimension.Scaling.CenterPoint.X;
            recent.Z = dimension.Scaling.CenterPoint.Y;
            recent.Zoom = dimension.Scaling.ZoomLevel;
        }

        private void RestoreView(RecentWorld recent)
        {
            if (recent.X is not double x || recent.Z is not double z) return;

            if (DimensionWidget is not null && recent.Dimension is string name && name != DimensionWidget.CurrentDimension.Namespace)
            {
                Dimension? dimension = DimensionWidget.Dimensions.Select(d => d.Dimension).Concat(DimensionWidget.ExtraDimensions).Cast<Dimension?>().FirstOrDefault(d => d!.Value.Namespace == name);
                if (dimension is Dimension found) DimensionWidget.CurrentDimension = found;
            }

            Scene.Map.ScaleBehaviour.SetScaling(new Scaling(recent.Zoom ?? 0, new Point(x, z)));

            // The map snaps to whole pixels; keeping the exact centre stops it creeping each time the world reopens
            if (Scene.Domain.CurrentWorld is not null) Scene.Domain.CurrentWorld.CurrentDimension.Scaling.CenterPoint = new Point(x, z);
        }
    }
}
