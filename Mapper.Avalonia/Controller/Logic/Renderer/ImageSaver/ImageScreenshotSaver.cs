using Avalonia;
using Avalonia.Media.Imaging;

namespace Mapper.Gui.Logic
{
    public class ImageScreenshotSaver
    {
        public CanvasControl Control { get; }

        public ImageScreenshotSaver(CanvasControl control)
        {
            Control = control;
        }

        // In screen pixels, so a scaled display gets a full-resolution screenshot
        public Size GetSize() 
        {
            double scaling = Control.RenderScaling;
            return new Size((int)(Control.ActualWidth * scaling), (int)(Control.ActualHeight * scaling));
        }
        public void SaveScreenshot(string path)
        {
            double scaling = Control.RenderScaling;
            using RenderTargetBitmap bitmap = new(new PixelSize((int)(Control.ActualWidth * scaling), (int)(Control.ActualHeight * scaling)), new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(Control);
            bitmap.Save(path);
        }
    }
}
