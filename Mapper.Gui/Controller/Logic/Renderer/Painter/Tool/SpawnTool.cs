using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    // Shades the blocks hostile mobs can spawn on, in the Overworld and the End. What's there is SpawnSpots' work,
    // shared with the Avalonia GUI; this draws each region on screen as a picture with a pixel a block
    public class SpawnTool : ToggleableTool, IPainter
    {
        public DrawingGroup? DrawingGroup { get; set; }

        public IScene Scene { get; }
        public SpawnSpots Spots { get; }

        private static readonly Color Shade = Color.FromArgb(130, 255, 40, 40);
        private readonly Dictionary<SpawnRegion, BitmapSource> _bitmaps = new(ReferenceEqualityComparer.Instance);

        public SpawnTool(IScene scene, SpawnSpots spots, IRenderInvoker renderer)
        {
            Scene = scene;
            Spots = spots;

            Scene.DimensionChanged += (sender, e) => Enabled = SpawnRules.HasSpawning(Scene.Dimension);
            Scene.ZoomChanged += (sender, e) => Enabled = !Scene.IsSceneEmpty && SpawnRules.HasSpawning(Scene.Dimension);
            Spots.Changed += () =>
            {
                if (IsTurnedOn && Enabled) renderer.Render();
            };

            Enabled = false;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (!Enabled || !IsTurnedOn || Scene.IsSceneEmpty || SpawnSpots.OnScreen(Scene) is not List<Coords> regions)
            {
                Forget([]);
                return;
            }
            Spots.Want(regions);

            // Blocks stay square when zoomed in, and blend when zoomed out
            if (DrawingGroup is not null) RenderOptions.SetBitmapScalingMode(DrawingGroup, Scene.ZoomCoefficient >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);

            double size = SpawnRegion.Size * Scene.ZoomCoefficient;
            HashSet<SpawnRegion> shown = new(ReferenceEqualityComparer.Instance);
            foreach (Coords coords in regions)
            {
                if (Spots.Get(coords) is not SpawnRegion region || region.Count == 0) continue;
                shown.Add(region);
                Point corner = Scene.XzToPointOnScreen(new XzPoint(coords.X * SpawnRegion.Size, coords.Z * SpawnRegion.Size));
                drawingContext.DrawImage(Bitmap(region), new Rect(corner, new Size(size, size)));
            }
            Forget(shown);
        }

        private BitmapSource Bitmap(SpawnRegion region)
        {
            if (_bitmaps.TryGetValue(region, out BitmapSource? bitmap)) return bitmap;

            const int size = SpawnRegion.Size;
            byte[] pixels = new byte[size * size * 4];
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!region[x, z]) continue;
                    int offset = (z * size + x) * 4;
                    pixels[offset] = Shade.B;
                    pixels[offset + 1] = Shade.G;
                    pixels[offset + 2] = Shade.R;
                    pixels[offset + 3] = Shade.A;
                }
            }

            bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return _bitmaps[region] = bitmap;
        }

        // Pictures of regions no longer on screen, or read again since, are let go
        private void Forget(HashSet<SpawnRegion> shown)
        {
            foreach (SpawnRegion region in _bitmaps.Keys.Where(r => !shown.Contains(r)).ToList()) _bitmaps.Remove(region);
        }
    }
}
