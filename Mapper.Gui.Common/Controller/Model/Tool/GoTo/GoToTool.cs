using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Linq;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    public class GoToTool : ToggleableTool
    {
        public ToolButton? Owner { get; set; }
        public Scene Scene { get; }
        public CanvasControl Canvas { get; }
        // Its pins, zones and shown signs are listed under the spawn points, while markup is shown
        public MarkupTool? Markup { get; set; }
        // And the structures of the dimension on show, nearest first, while the structure overlay is on
        public WorldStructures? Structures { get; set; }
        private const int MostStructures = 200;

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
            if (Markup is { Enabled: true, ShowMarkup: true })
            {
                goToWindow.AddPlaces(Markup.Places().Select(item =>
                {
                    var (x, z) = MarkupTool.CentreOf(item);
                    return (MarkupTool.Title(item), new XzPoint(x, z));
                }));
                // The signs shown, by everything they say, so typing a word finds them
                goToWindow.AddPlaces(Markup.Editor.Document.SignsIn(Markup.View.Dimension)
                    .OrderBy(s => s.Text, StringComparer.CurrentCultureIgnoreCase)
                    .Select(s => ($"Sign: {(s.Text.Length > 0 ? s.Text : "blank")}", new XzPoint(s.X + 0.5, s.Z + 0.5))));
                // And the named mobs shown, by name and kind
                goToWindow.AddPlaces(Markup.Editor.Document.PetsIn(Markup.View.Dimension)
                    .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => ($"{p.Name} ({p.Kind.ToLowerInvariant()})", new XzPoint(p.X + 0.5, p.Z + 0.5))));
            }
            if (Structures is { Shown: true })
            {
                goToWindow.AddPlaces(Structures.Current
                    .OrderBy(s => (s.X - centerPoint.X) * (s.X - centerPoint.X) + (s.Z - centerPoint.Z) * (s.Z - centerPoint.Z))
                    .Take(MostStructures)
                    .Select(s => (s.Name, new XzPoint(s.X, s.Z))));
            }

            void Closed()
            {
                _isActive = false;

                if (goToWindow.DialogClosed) return;
                Scene.Map.SetCenterPoint(goToWindow.SelectedPoint);
            }

#if AVALONIA
            // A flyout under the button; WPF places a borderless window there, which Wayland doesn't allow
            Flyout flyout = new() { Content = goToWindow, Placement = PlacementMode.Bottom };
            goToWindow.PointSelected += (s, ee) => flyout.Hide();
            flyout.Closed += (s, ee) => Closed();

            flyout.ShowAt(Owner.Button);
#else
            Point startupLocation = Owner.Button.PointToScreen(new(0, 0)).CalibrateToDpiScale();
            startupLocation.Y += Owner.Button.ActualHeight;
            startupLocation.X += Owner.Button.ActualWidth / 2 - goToWindow.Width / 2;

            goToWindow.Top = startupLocation.Y;
            goToWindow.Left = startupLocation.X;

            goToWindow.Show();
            goToWindow.Closing += (s, ee) => Closed();
#endif
        }
    }
}
