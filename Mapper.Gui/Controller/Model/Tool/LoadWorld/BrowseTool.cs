using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    public class BrowseTool : ToggleableTool
    {
        public Scene Scene { get; }
        public MapViewer MapViewer { get; }
        public WorldOpener Opener { get; }

        private bool _isActive = false;

        public BrowseTool(Scene scene, MapViewer mapViewer, WorldOpener opener)
        {
            Scene = scene;
            MapViewer = mapViewer;
            Opener = opener;

            if (MapViewer.HasBeenShown) Start();
            else MapViewer.Shown += (sender, e) => Start();

            OnTurnedOn += TurnedOn;
        }

        // The browser opens at startup unless a world was named on the command line
        private async void Start()
        {
            string? startup = Opener.StartupWorld;
            Opener.StartupWorld = null;

            if (startup is not null)
            {
                if (await Opener.TryOpen(startup)) return;
                MessageBox.Show(MapViewer, $"Couldn't open {Path.GetFileName(startup)} as a Minecraft world.", "Mapee", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            IsTurnedOn = true;
        }

        private void TurnedOn(bool isTurnedOn)
        {
            if (_isActive) return;

            _isActive = true;
            IsTurnedOn = false;

            OpenWindow();
        }

        private void OpenWindow()
        {
            _isActive = true;

            WorldBrowser browser = new(AnyEditionLevelReader.DefaultWorldDirectories(), new AnyEditionLevelReader(), Opener.Settings.RecentWorlds.Select(w => w.Path).ToList());
            browser.ShowDialog();

            _isActive = false;

            if (browser.DialogClosed) return;
            Opener.Open(browser.Selected.Source, browser.Selected.Level);
        }
    }
}
