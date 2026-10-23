using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    // Shades the blocks hostile mobs can spawn on, in the Overworld and the End. What's there is SpawnSpots' work,
    // shared with the WPF GUI; this draws each region on screen as a picture with a pixel a block
    public class SpawnTool : ToggleableTool, IPainter
    {
        public IScene Scene { get; }
        public SpawnSpots Spots { get; }

        private static readonly (byte R, byte G, byte B, byte A) Shade = (255, 40, 40, 130);
        private readonly Dictionary<SpawnRegion, WriteableBitmap> _bitmaps = new(ReferenceEqualityComparer.Instance);

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
            if (!Enabled || !IsTurnedOn || Scene.IsSceneEmpty)
            {
                Forget([]);
                return;
            }

            if (SpawnSpots.OnScreen(Scene) is not List<Coords> regions)
            {
                Forget([]);
                return;
            }
            Spots.Want(regions);

            double size = SpawnRegion.Size * Scene.ZoomCoefficient;
            HashSet<SpawnRegion> shown = new(ReferenceEqualityComparer.Instance);
            using (drawingContext.PushRenderOptions(new RenderOptions() { BitmapInterpolationMode = Scene.ZoomCoefficient >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
            {
                foreach (Coords coords in regions)
                {
                    if (Spots.Get(coords) is not SpawnRegion region || region.Count == 0) continue;
                    shown.Add(region);
                    Point corner = Scene.XzToPointOnScreen(new XzPoint(coords.X * SpawnRegion.Size, coords.Z * SpawnRegion.Size));
                    drawingContext.DrawImage(Bitmap(region), new Rect(corner, new Size(size, size)));
                }
            }
            Forget(shown);
        }

        private WriteableBitmap Bitmap(SpawnRegion region)
        {
            if (_bitmaps.TryGetValue(region, out WriteableBitmap? bitmap)) return bitmap;

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

            bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                for (int z = 0; z < size; z++) Marshal.Copy(pixels, z * size * 4, buffer.Address + z * buffer.RowBytes, size * 4);
            }
            return _bitmaps[region] = bitmap;
        }

        // Pictures of regions no longer on screen, or read again since, are let go
        private void Forget(HashSet<SpawnRegion> shown)
        {
            foreach (SpawnRegion region in _bitmaps.Keys.Where(r => !shown.Contains(r)).ToList())
            {
                _bitmaps[region].Dispose();
                _bitmaps.Remove(region);
            }
        }
    }
}
