using System;
using System.Windows.Media.Imaging;

namespace Mapper.Gui
{
    // Images built into the app, by pack URI path ("/Resources/Image/..."); the Avalonia GUI has the same method
    public static class AppImages
    {
        public static BitmapImage Load(string path)
        {
            return new BitmapImage(new Uri(path, UriKind.Relative));
        }
    }
}
