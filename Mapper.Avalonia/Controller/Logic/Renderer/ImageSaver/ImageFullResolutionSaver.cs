using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    public class ImageFullResolutionSaver
    {
        public Scene Scene { get; }
        public Renderer Renderer { get; }

        public ImageFullResolutionSaver(Scene scene, Renderer renderer)
        {
            Scene = scene;
            Renderer = renderer;
        }

        public Size GetSize(FullResolutionImageArgs args)
        {
            XzRange visible = ProvideVisibleArea(args);
            Rect area = ProvideArea(args, visible);

            return new Size((int)area.Width, (int)area.Height);
        }
        public void SaveAsFullResolutionImage(string outputFile, FullResolutionImageArgs args)
        {
            XzRange visible = ProvideVisibleArea(args);
            Rect area = ProvideArea(args, visible);

            visible = new XzRange(
                MathUtilities.FindSectionY((int)visible.TopLeftPoint.X, 512),
                MathUtilities.FindSectionY((int)visible.TopLeftPoint.Z, 512),
                MathUtilities.FindSectionY((int)visible.BottomRightPoint.X, 512),
                MathUtilities.FindSectionY((int)visible.BottomRightPoint.Z, 512));

            using RenderTargetBitmap bitmap = new(new PixelSize((int)area.Width, (int)area.Height), new Vector(96, 96));
            using (DrawingContext drawingContext = bitmap.CreateDrawingContext())
            using (drawingContext.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
            using (drawingContext.PushClip(area))
            using (drawingContext.PushRenderOptions(new RenderOptions() { BitmapInterpolationMode = BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased }))
            {
                Draw(drawingContext, visible, area, args);
            }

            bitmap.Save(outputFile);
        }

        private XzRange ProvideVisibleArea(FullResolutionImageArgs args)
        {
            XzRange visible = GetVisibleArea();
            if (!args.ClipArea) return visible;

            XzRange loadedArea = Scene.RegionLoader.LoadedArea;
            if (Scene.Domain.CurrentWorld is null) loadedArea = new XzRange(0, 0, 0, 0);

            double topLeftX = visible.TopLeftPoint.X, topLeftZ = visible.TopLeftPoint.Z;

            if (loadedArea.TopLeftPoint.X > topLeftX) topLeftX = loadedArea.TopLeftPoint.X;
            if (loadedArea.TopLeftPoint.Z > topLeftZ) topLeftZ = loadedArea.TopLeftPoint.Z;

            double bottomRightX = visible.BottomRightPoint.X, bottomRightZ = visible.BottomRightPoint.Z;

            if (bottomRightX > loadedArea.BottomRightPoint.X) bottomRightX = loadedArea.BottomRightPoint.X;
            if (bottomRightZ > loadedArea.BottomRightPoint.Z) bottomRightZ = loadedArea.BottomRightPoint.Z;

            return new XzRange(new XzPoint(topLeftX, topLeftZ), new XzPoint(bottomRightX, bottomRightZ));
        }
        private XzRange GetVisibleArea()
        {
            return new XzRange()
            {
                TopLeftPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.TopLeftPoint),
                BottomRightPoint = Map.TransformXyToXz(Scene.Map.ScaleBehaviour.BottomRightPoint)
            };
        }
        private Rect ProvideArea(FullResolutionImageArgs args, XzRange visible)
        {
            Point topLeft = Scene.Map.ScaleBehaviour.TopLeftPoint;
            Point bottomRight = Scene.Map.ScaleBehaviour.BottomRightPoint;
            if (!args.ClipArea) return new Rect(topLeft, new Point(bottomRight.X + 1, bottomRight.Y + 1));

            double left = topLeft.X, top = topLeft.Y, right = bottomRight.X, bottom = bottomRight.Y;

            if (left < visible.TopLeftPoint.X) left = visible.TopLeftPoint.X;
            if (top < visible.TopLeftPoint.Z) top = visible.TopLeftPoint.Z;

            if (right > visible.BottomRightPoint.X) right = visible.BottomRightPoint.X;
            if (bottom > visible.BottomRightPoint.Z) bottom = visible.BottomRightPoint.Z;

            return new Rect(new Point(left, top), new Point(right + 1, bottom + 1));
        }

        private void Draw(DrawingContext drawingContext, XzRange visible, Rect area, FullResolutionImageArgs args)
        {
            Background background = Scene.Domain.CurrentWorld?.CurrentDimension.RenderSettings.Background ?? Background.Empty;
            background = new Background()
            {
                Type = args.CheckerPatternEnabled ? BackgroundType.Checker : BackgroundType.Solid,
                CheckedColorPair = background.CheckedColorPair,
                SolidColor = args.BackgroundColor
            };

            BackgroundPaintArgs backgroundPaintArgs = new()
            {
                BackgroundArea = area,
                Background = background
            };

            ScenePaintArgs scenePaintArgs = new()
            {
                Scale = new ScalePaintArgs()
                {
                    ZoomCoefficient = 1,
                    ZoomLevel = 0,
                    Offset = default,
                    LevelIncrement = 1
                },
                VisibleArea = visible
            };

            Renderer.BackgroundPainter.Paint(drawingContext, backgroundPaintArgs);
            Renderer.ScenePainter.Paint(drawingContext, scenePaintArgs);
        }
    }
}
