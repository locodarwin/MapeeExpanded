using Mapper.Gui.Model;
using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Mapper.Gui.Logic
{
    public class TilePainter : IPainter<BackgroundPaintArgs>
    {
        public ICheckerImageGenerator CheckerImageGenerator { get; set; }

        private Bitmap? _prevImage;
        private ColorPair _prevColor;

        public TilePainter() 
        {
            CheckerImageGenerator = new CheckerImageGenerator();
        }

        public void Paint(DrawingContext drawingContext, BackgroundPaintArgs args)
        {
            Bitmap image = GetImage(args);
            double width = image.PixelSize.Width, height = image.PixelSize.Height;

            int xCount = (int)Math.Ceiling(args.BackgroundArea.Width / width);
            int yCount = (int)Math.Ceiling(args.BackgroundArea.Height / height);

            using (drawingContext.PushRenderOptions(new RenderOptions() { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            {
                for (int y = 0; y < yCount; y++)
                {
                    for (int x = 0; x < xCount; x++)
                    {
                        drawingContext.DrawImage(image, new Rect(x * width + args.BackgroundArea.TopLeft.X, y * height + args.BackgroundArea.TopLeft.Y, width, height));
                    }
                }
            }
        }
        private Bitmap GetImage(BackgroundPaintArgs args) 
        {
            if (_prevImage is null || _prevColor != args.Background.CheckedColorPair) 
            {
                const int CHECKER_SIZE = 9, TILES_COUNT = 64;
                PixelSize size = new(CHECKER_SIZE * TILES_COUNT, CHECKER_SIZE * TILES_COUNT);

                _prevImage = CheckerImageGenerator.Generate(size, CHECKER_SIZE, args.Background.CheckedColorPair);
            }

            _prevColor = args.Background.CheckedColorPair;
            return _prevImage;
        }
    }
}
