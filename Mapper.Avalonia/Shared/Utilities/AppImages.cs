using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

namespace Mapper.Gui
{
    // Images built into the app, by the path the WPF GUI's pack URIs use ("/Resources/Image/...")
    public static class AppImages
    {
        public static Bitmap Load(string path)
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://Mapee/{path.TrimStart('/')}")));
        }
    }
}
