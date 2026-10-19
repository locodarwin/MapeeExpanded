using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.IO;
using System.Linq;
using WorldEditor;
#if AVALONIA
#else
using System.Windows;
#endif

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
                StartupLog.Mark($"Opening {startup}");
                bool opened = await Opener.TryOpen(startup);
                StartupLog.Mark(opened ? "World opened" : "World didn't open");
                if (opened)
                {
                    StartupLog.StopAssemblyLog();
                    return;
                }
                await MessageWindow.Show($"Couldn't open {Path.GetFileName(startup)} as a Minecraft world.", "Mapee", MapViewer);
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

        private async void OpenWindow()
        {
            _isActive = true;

            StartupLog.Mark("Opening the world browser");
            WorldBrowser browser = new(AnyEditionLevelReader.DefaultWorldDirectories(), new AnyEditionLevelReader(), Opener.Settings.RecentWorlds.Select(w => w.Path).ToList());
            StartupLog.Mark("World browser made");
#if AVALONIA
            browser.Opened += (sender, e) => StartupLog.StopAssemblyLog();
#else
            browser.ContentRendered += (sender, e) => StartupLog.StopAssemblyLog();
#endif
            await AppWindows.ShowDialog(browser, MapViewer);

            _isActive = false;

            if (browser.DialogClosed) return;
            Opener.Open(browser.Selected.Source, browser.Selected.Level);
        }
    }
}
