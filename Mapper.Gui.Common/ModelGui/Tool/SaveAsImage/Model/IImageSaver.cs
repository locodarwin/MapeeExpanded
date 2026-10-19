using System;
using System.Collections.Generic;
using System.Threading;
#if AVALONIA
using Avalonia;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Model
{
    public interface IImageSaver
    {
        FullResolutionImageArgs DefaultFullResArgs { get; }

        Size GetScreenshotSize();
        Size GetFullResolutionSize(FullResolutionImageArgs args);

        void SaveAsScreenshot(string path);
        void SaveAsFullResolution(string path, FullResolutionImageArgs args);

        bool CanSaveWebMap { get; }
        string WebMapDescription { get; }
        // What an export into the folder would write over; empty if nothing
        IList<string> WebMapWillReplace(string folder);
        // Takes the view's settings now, on the UI thread; the export returned is slow and belongs off it
        Action<IProgress<WebMapProgress>?, CancellationToken> PrepareWebMap(string folder, WebMapOptions options);
    }
}
