using Mapper.Gui.Logic;
using Mapper.Gui.Model;

namespace Mapper.Gui.Controller
{
    public class ExportAsImageTool : ToggleableTool
    {
        public IImageSaver ImageSaver { get; set; }
        public MapViewer MapViewer { get; }

        private bool _isActive = false;

        public ExportAsImageTool(Renderer renderer, MapViewer mapViewer)
        {
            MapViewer = mapViewer;
            ImageSaver = new ImageSaver(renderer.Scene, renderer);
            OnTurnedOn += TurnedOn;
        }

        private void TurnedOn(bool isTurnedOn)
        {
            if (_isActive) return;

            _isActive = true;
            IsTurnedOn = false;

            OpenWindow();
        }

        private async void OpenWindow()
        {
            _isActive = true;

            SaveAsImageControl window = new SaveAsImageControl(ImageSaver);
            await AppWindows.ShowDialog(window, MapViewer);

            _isActive = false;
        }
    }
}
