using Mapper.Gui.Model;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    public class SlimeChunkTool : ToggleableTool, IPainter
    {
        public IScene Scene { get; }
        public ISlimeChunkChecker SlimeChunkChecker { get; set; }

        public Brush ChunkBrush { get; set; }

        private static readonly int CHUNK_SIZE = 16;
        private byte[]? _buffer = null;

        public SlimeChunkTool(IScene scene, ISlimeChunkChecker slimeChunkChecker)
        {
            Scene = scene;
            SlimeChunkChecker = slimeChunkChecker;

            ChunkBrush = new SolidColorBrush(Color.FromArgb(222, 60, 180, 70));

            Scene.ZoomChanged += Scene_ZoomChanged;
            Scene.DimensionChanged += Scene_DimensionChanged;

            Enabled = false;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (!Enabled || !IsTurnedOn || Scene.IsSceneEmpty) return;
            if (!IsZoomAppropriate()) return;

            Rect area = GetArea(CHUNK_SIZE);

            int xCount = IntervalMathUtilities.GetIntervalCount((int)area.X, (int)area.BottomRight.X, CHUNK_SIZE);
            int zCount = IntervalMathUtilities.GetIntervalCount((int)area.Y, (int)area.BottomRight.Y, CHUNK_SIZE);

            int xStart = MathUtilities.FindSectionY((int)area.X, CHUNK_SIZE);
            int zStart = MathUtilities.FindSectionY((int)area.Y, CHUNK_SIZE);

            int length = xCount * zCount * 4;
            if (_buffer == null || length > _buffer.Length) _buffer = new byte[length];

            byte[] pixelArray = _buffer;

            Color empty = Color.FromArgb(0, 0, 0, 0);

            Parallel.For(0, zCount, z =>
            {
                for (int x = 0; x < xCount; x++)
                {
                    Color color = Color.FromArgb(192, 75, 160, 84);
                    if (!SlimeChunkChecker.IsSlimeChunk(xStart + x, zStart + z)) color = empty;

                    int offset = (z * xCount + x) * 4;
                    pixelArray[offset] = color.B;
                    pixelArray[offset + 1] = color.G;
                    pixelArray[offset + 2] = color.R;
                    pixelArray[offset + 3] = color.A;
                }
            });

            using WriteableBitmap bitmap = new(new PixelSize(xCount, zCount), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                for (int z = 0; z < zCount; z++)
                {
                    Marshal.Copy(pixelArray, z * xCount * 4, buffer.Address + z * buffer.RowBytes, xCount * 4);
                }
            }

            double zoom = Scene.ZoomCoefficient;
            using (drawingContext.PushRenderOptions(new RenderOptions() { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                drawingContext.DrawImage(bitmap, new Rect(Scene.XzToPointOnScreen(new XzPoint((int)area.TopLeft.X, (int)area.TopLeft.Y)), new Size(xCount * CHUNK_SIZE * zoom, zCount * CHUNK_SIZE * zoom)));
            }
        }
        private Rect GetArea(int interval)
        {
            Point topLeft = GetLocation(Scene.XzToXy(Scene.TopLeft), interval);

            XzPoint xzPoint = Scene.BottomRight;
            Point bottomRight = Scene.XzToXy(new XzPoint(xzPoint.X + 1, xzPoint.Z + 1));

            return new Rect(topLeft, bottomRight);
        }
        private static Point GetLocation(Point scenePoint, int interval)
        {
            return new Point(MathUtilities.FindSectionY((int)scenePoint.X, interval) * interval,
                MathUtilities.FindSectionY((int)scenePoint.Y, interval) * interval);
        }

        private void Scene_ZoomChanged(object? sender, EventArgs e)
        {
            if (!Scene.IsSceneEmpty)
            {
                Enabled = Scene.Dimension == Dimension.Overworld && IsZoomAppropriate();
            }
            else
            {
                Enabled = false;
            }
        }
        private void Scene_DimensionChanged(object? sender, EventArgs e)
        {
            SetEnabled();
        }

        private void SetEnabled() 
        {
            Enabled = Scene.Dimension == Dimension.Overworld;
        }
        private bool IsZoomAppropriate()
        {
            return Scene.ZoomLevel > -5;
        }
    }
}
