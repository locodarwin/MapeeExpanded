using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Mapper.Gui.Controller
{
    public class GoToTool : ToggleableTool
    {
        public ToolButton? Owner { get; set; }
        public Scene Scene { get; }
        public CanvasControl Canvas { get; }

        private bool _isActive = false;

        public GoToTool(Scene scene, CanvasControl canvas) 
        {
            Scene = scene;
            Canvas = canvas;

            OnTurnedOn += TurnedOn;
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
            if (Owner is null || Owner.Button is null) return;

            XzPoint playerPos = new(), playSpawn = new(), worldSpawn = new();
            if (Scene.Domain.CurrentWorld is not null)
            {
                playerPos = XzPoint.FromVector(Scene.Domain.CurrentWorld.Level.Player.Position);
                playSpawn = XzPoint.FromVector(Scene.Domain.CurrentWorld.Level.Player.Spawn);
                worldSpawn = XzPoint.FromVector(Scene.Domain.CurrentWorld.Level.WorldGen.WorldSpawn);
            }

            XzPoint centerPoint = Scene.Map.TransformPointOnScreenToXz(new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2));
            GoToWindow goToWindow = new(centerPoint, playerPos, playSpawn, worldSpawn);

            // A flyout under the button; the WPF GUI places a borderless window there, which Wayland doesn't allow
            Flyout flyout = new() { Content = goToWindow, Placement = PlacementMode.Bottom };
            goToWindow.PointSelected += (s, ee) => flyout.Hide();
            flyout.Closed += (s, ee) =>
            {
                _isActive = false;

                if (goToWindow.DialogClosed) return;
                Scene.Map.SetCenterPoint(goToWindow.SelectedPoint);
            };

            flyout.ShowAt(Owner.Button);
        }
    }
}
