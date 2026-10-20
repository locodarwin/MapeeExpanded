using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Threading;
#if AVALONIA
using Avalonia;
using Avalonia.Media;
#else
using System.Windows;
using System.Windows.Media;
#endif

namespace Mapper.Gui.Controller
{
    public class ImageSaver : IImageSaver
    {
        public FullResolutionImageArgs DefaultFullResArgs => new FullResolutionImageArgs() 
        {
            ClipArea = true,
            CheckerPatternEnabled = true,
            BackgroundColor = Colors.Black,
            IncludeMarkup = true
        };

        public ImageScreenshotSaver ScreenshotSaver { get; set; }
        public ImageFullResolutionSaver ImageFullResolutionSaver { get; set; }
        public WebMapSaver WebMapSaver { get; set; }

        private readonly Renderer _renderer;

        public ImageSaver(Scene scene, Renderer renderer) 
        {
            _renderer = renderer;
            ScreenshotSaver = new ImageScreenshotSaver(renderer.GraphicsCanvas);
            ImageFullResolutionSaver = new ImageFullResolutionSaver(scene, renderer);
            WebMapSaver = new WebMapSaver(scene) { Markup = () => renderer.MarkupPainter?.Tool is { ShowMarkup: true } tool ? tool.Editor.Document : null };
        }

        public Size GetScreenshotSize()
        {
            return ScreenshotSaver.GetSize();
        }
        public Size GetFullResolutionSize(FullResolutionImageArgs args)
        {
            return ImageFullResolutionSaver.GetSize(args);
        }

        public bool HasMarkup => _renderer.MarkupPainter?.Tool is { ShowMarkup: true } tool && !tool.Editor.Document.IsEmpty;

        // Leaving markup out means drawing the map once without it
        public void SaveAsScreenshot(string path, bool includeMarkup = true)
        {
            MarkupPainter? painter = includeMarkup ? null : _renderer.MarkupPainter;
            if (painter is not null)
            {
                painter.Hidden = true;
                _renderer.Render();
            }

            try
            {
                ScreenshotSaver.SaveScreenshot(path);
            }
            finally
            {
                if (painter is not null)
                {
                    painter.Hidden = false;
                    _renderer.Render();
                }
            }
        }
        public void SaveAsFullResolution(string path, FullResolutionImageArgs args)
        {
            ImageFullResolutionSaver.SaveAsFullResolutionImage(path, args);
        }

        public bool CanSaveWebMap => WebMapSaver.CanSave;
        public string WebMapDescription => WebMapSaver.Describe();
        public IList<string> WebMapWillReplace(string folder) => WebMapSaver.WillReplace(folder);
        public Action<IProgress<WebMapProgress>?, CancellationToken> PrepareWebMap(string folder, WebMapOptions options)
        {
            return WebMapSaver.Prepare(folder, options);
        }
    }
}
