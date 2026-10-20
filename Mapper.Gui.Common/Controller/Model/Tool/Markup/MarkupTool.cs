using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    // The Markup button: turned on, the markup bar shows and clicks on the map add and change pins and zones. Each
    // world's markup is read when it opens and saved after every change. Whether markup is drawn at all is the bar's
    // Show switch, remembered in the settings
    public class MarkupTool : ToggleableTool
    {
        public Scene Scene { get; }
        public WorldOpener WorldOpener { get; }
        public AppSettings Settings { get; }
        public MarkupStore Store { get; }
        public MarkupEditor Editor { get; }
        public SceneMarkupView View { get; }

        // The world's markup was read, or changed; the Places list should be filled again
        public event EventHandler? PlacesChanged;

        public MarkupTool(Scene scene, WorldOpener worldOpener, AppSettings settings, IRenderInvoker renderInvoker)
        {
            Scene = scene;
            WorldOpener = worldOpener;
            Settings = settings;
            Store = MarkupStore.ForSettings(settings.File);
            View = new SceneMarkupView(scene);
            Editor = new MarkupEditor(View) { Visible = settings.ShowMarkup };

            Editor.Changed += (sender, e) => renderInvoker.Render();
            Editor.Edited += (sender, e) =>
            {
                Store.Save(Editor.Document);
                PlacesChanged?.Invoke(this, EventArgs.Empty);
            };

            Scene.WorldChanged += (old, current) => Load(current.Level);
            Scene.DimensionChanged += (old, current) =>
            {
                Editor.Reset();
                PlacesChanged?.Invoke(this, EventArgs.Empty);
            };

            OnTurnedOn += isTurnedOn =>
            {
                if (isTurnedOn) ShowMarkup = true;
                Editor.Mode = isTurnedOn ? MarkupMode.Pin : MarkupMode.View;
            };

            Enabled = false;
        }

        public bool ShowMarkup
        {
            get => Editor.Visible;
            set
            {
                if (Editor.Visible == value) return;
                Editor.Visible = value;
                if (!value) IsTurnedOn = false;

                Settings.ShowMarkup = value;
                Settings.Save();
            }
        }

        private void Load(Level level)
        {
            // A world put straight into the scene (as GuiProbe's load: step does) is known by its folder
            string? source = WorldOpener.SourceOf(level) ?? level.Directory;
            Editor.Load(source is null ? new MarkupDocument() : Store.Load(source, level.WorldName));
            Enabled = source is not null;
            if (!Enabled) IsTurnedOn = false;

            PlacesChanged?.Invoke(this, EventArgs.Empty);
        }

        // The pins and zones of the dimension on show, pins first, each by label
        public IList<MarkupItem> Places()
        {
            string dimension = View.Dimension;
            return [.. Editor.Document.PinsIn(dimension).OrderBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).Cast<MarkupItem>()
                .Concat(Editor.Document.ZonesIn(dimension).OrderBy(z => z.Label, StringComparer.CurrentCultureIgnoreCase))];
        }

        public static string Describe(MarkupItem item) => item switch
        {
            MapPin pin => $"{pin.X}; {pin.Z}",
            MapZone zone => ZoneGeometry.Describe(zone.Points),
            _ => string.Empty
        };

        public static string Title(MarkupItem item) => item.Label.Length > 0 ? item.Label : item is MapPin ? "Unnamed pin" : "Unnamed zone";

        // The middle of a pin's block, or where a zone's label goes, in blocks
        public static (double X, double Z) CentreOf(MarkupItem item) => item switch
        {
            MapPin pin => (pin.X + 0.5, pin.Z + 0.5),
            MapZone zone => ZoneGeometry.LabelPoint(zone.Points),
            _ => (0, 0)
        };

        public void GoTo(MarkupItem item)
        {
            var (x, z) = CentreOf(item);
            Scene.Map.SetCenterPoint(new XzPoint(x, z));
        }

        // Where on the map surface a pin or zone's details should be shown
        public (double X, double Y) AnchorOf(MarkupItem item)
        {
            var (x, z) = CentreOf(item);
            return View.ToScreen(x, z);
        }

        public void Export(string file) => MarkupStore.Write(Editor.Document, file);

        // Returns what to tell the user
        public string Import(string file)
        {
            MarkupDocument other;
            try
            {
                other = MarkupStore.Read(file);
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.IO.InvalidDataException)
            {
                return $"Couldn't read markup from that file: {e.Message}";
            }

            var (pins, zones) = Editor.Import(other);
            if (pins + zones == 0) return other.IsEmpty ? "That file holds no markup." : "Everything in that file is already here.";
            return $"Added {Count(pins, "pin")} and {Count(zones, "zone")}.";
        }

        private static string Count(int count, string what) => $"{count} {what}{(count == 1 ? "" : "s")}";
    }

    // The markup editor's view of the map: blocks to points on the map surface and back, as the map draws them
    public class SceneMarkupView : IMarkupView
    {
        public Scene Scene { get; }

        public SceneMarkupView(Scene scene)
        {
            Scene = scene;
        }

        public string Dimension => Scene.Domain.CurrentWorld?.CurrentDimension.Dimension.Namespace ?? WorldEditor.Dimension.Overworld.Namespace;

        public (double X, double Y) ToScreen(double x, double z)
        {
            ScaleBehaviour scale = Scene.Map.ScaleBehaviour;
            return ((x - scale.TopLeftPoint.X) * scale.CurrentZoomCoefficient, (z - scale.TopLeftPoint.Y) * scale.CurrentZoomCoefficient);
        }

        public (double X, double Z) ToWorld(double x, double y)
        {
            ScaleBehaviour scale = Scene.Map.ScaleBehaviour;
            return (scale.TopLeftPoint.X + x / scale.CurrentZoomCoefficient, scale.TopLeftPoint.Y + y / scale.CurrentZoomCoefficient);
        }
    }

    // Draws a markup frame worked out for some other view, such as a full-resolution image at a block a pixel
    public class FixedMarkupView : IMarkupView
    {
        public string Dimension { get; }
        private readonly double _left, _top, _scale;

        public FixedMarkupView(string dimension, double left, double top, double scale)
        {
            Dimension = dimension;
            (_left, _top, _scale) = (left, top, scale);
        }

        public (double X, double Y) ToScreen(double x, double z) => ((x - _left) * _scale, (z - _top) * _scale);
        public (double X, double Z) ToWorld(double x, double y) => (_left + x / _scale, _top + y / _scale);
    }
}
