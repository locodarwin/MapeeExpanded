using Mapper.Gui.Logic;
using Mapper.Gui.Model;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    public class RenderSettingsTool : ToggleableTool
    {
        public Scene Scene { get; set; }

        private bool _isActive = false;
        private Window? _window;

        public RenderSettingsTool(Scene scene)
        {
            Scene = scene;
            Enabled = Scene.Domain.CurrentWorld is not null;
            
            Scene.WorldBeginChange += Scene_WorldBeginChange;
            Scene.WorldChanged += Scene_WorldChanged;
            Scene.DimensionBeginChange += Scene_DimensionBeginChange;
            Scene.DimensionChanged += Scene_DimensionChanged;
            Scene.StyleBeginReset += Scene_StyleReset;

            OnTurnedOn += TurnedOn;
            AppWindows.OnExit(Close);
        }

        private void Scene_WorldBeginChange(WorldDomain? old, WorldDomain current)
        {
            Close();
        }
        private void Scene_WorldChanged(WorldDomain? old, WorldDomain current) 
        {
            Enabled = Scene.Domain.CurrentWorld is not null;
        }

        private void Scene_DimensionBeginChange(DimensionDomain old, DimensionDomain current) 
        {
            Close();
        }
        private void Scene_DimensionChanged(DimensionDomain old, DimensionDomain current) 
        {
            Close();
        }
        private void Scene_StyleReset(Logic.Style old, Logic.Style current)
        {
            Close();
        }

        private void TurnedOn(bool isTurnedOn)
        {
            if (_isActive) 
            {
                if (IsTurnedOn) IsTurnedOn = false;
                else _window?.Activate();

                return;
            }

            _isActive = true;
            IsTurnedOn = false;

            OpenWindow();
        }

        private void OpenWindow()
        {
            if (Scene.Domain.CurrentWorld is null) return;

            _isActive = true;

            RenderSettingsControl window = new(Scene.Domain.CurrentWorld.CurrentDimension.RenderSettings, Scene.Domain.CurrentWorld.CurrentDimension.Dimension.Name);
            _window = window;
            AppWindows.ShowTool(window);

            window.RenderProfileUpdated += (sender, e) => 
            {
                Profile profile = new(
                    Scene.Domain.CurrentWorld.Style.AssetPack,
                    Scene.Domain.CurrentWorld.CurrentDimension.HeightmapSettings,
                    window.RenderSettings);

                Scene.UpdateProfile(profile);
            };
            window.Closing += (sender, e) => _isActive = false;
        }
        private void Close() 
        {
            if(_isActive) _window?.Close();
        }
    }
}
