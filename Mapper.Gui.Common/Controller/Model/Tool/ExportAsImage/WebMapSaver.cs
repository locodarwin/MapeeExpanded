using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    // Exports the dimension on screen as a zoomable web map, drawn as the view draws it: same style, same
    // height cut. The settings are taken when the export starts, so changing the view meanwhile doesn't
    // change the export
    public class WebMapSaver
    {
        public Scene Scene { get; }

        public WebMapSaver(Scene scene)
        {
            Scene = scene;
        }

        public bool CanSave => Scene.Domain.CurrentWorld is not null;

        // Call on the UI thread; the returned export can run anywhere
        public Action<IProgress<WebMapProgress>?, CancellationToken> Prepare(string folder, WebMapOptions options)
        {
            WorldDomain world = Scene.Domain.CurrentWorld ?? throw new InvalidOperationException("No world is open");
            DimensionDomain dimension = world.CurrentDimension;

            ChunkMapperPack pack = new(Scene.Domain.DefaultAssetPack);
            pack.UpdatePack(Scene.GetDrawingProfile(world, dimension, out ScanType scanType), scanType);

            WebMapInfo info = new()
            {
                Title = world.Level.WorldName,
                Subtitle = Describe(world, dimension),
                Markers = options.IncludeMarkers ? Markers(world.Level, dimension.Dimension) : []
            };

            return (progress, cancellation) =>
            {
                WebMapWriter writer = new(folder);
                SceneInfo scene = new(world.Level, dimension.Dimension);
                WebMapExporter.Export(pack, scene, writer, info, progress, cancellation);
            };
        }

        public string Describe()
        {
            if (Scene.Domain.CurrentWorld is not WorldDomain world) return string.Empty;
            return Describe(world, world.CurrentDimension);
        }

        // "Overworld · Default · slice at Y 30"
        private static string Describe(WorldDomain world, DimensionDomain dimension)
        {
            List<string> parts = [dimension.Dimension.Name, world.Style.Metadata.Name];
            if (dimension.Cut is HeightCut cut) parts.Add(cut.Caves ? $"caves below Y {cut.Y}" : $"slice at Y {cut.Y}");
            return string.Join(" · ", parts);
        }

        private static List<WebMapMarker> Markers(Level level, Dimension dimension)
        {
            List<WebMapMarker> markers = [];
            if (dimension == Dimension.Overworld) markers.Add(new WebMapMarker("Spawn", level.WorldGen.WorldSpawn.X, level.WorldGen.WorldSpawn.Z));
            if (level.Player.Dimension == dimension) markers.Add(new WebMapMarker("Player", level.Player.Position.X, level.Player.Position.Z));
            return markers;
        }
    }
}
