using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    // Exports the world's Overworld, Nether and End, those it has, as one zoomable web map, each drawn as the
    // view draws it: same style, and for a dimension with the height panel on, the same height cut. The settings
    // are taken when the export starts, so changing the view meanwhile doesn't change the export
    public class WebMapSaver
    {
        public Scene Scene { get; }

        public WebMapSaver(Scene scene)
        {
            Scene = scene;
        }

        public bool CanSave => Scene.Domain.CurrentWorld is not null;

        // What an export into the folder writes over; empty when the folder is empty or holds nothing it would touch
        public IList<string> WillReplace(string folder)
        {
            return WebMapFolder.WillReplace(folder, Targets().Select(WebMapFolder.IdOf));
        }

        // Call on the UI thread; the returned export can run anywhere
        public Action<IProgress<WebMapProgress>?, CancellationToken> Prepare(string folder, WebMapOptions options)
        {
            WorldDomain world = Scene.Domain.CurrentWorld ?? throw new InvalidOperationException("No world is open");

            List<WebMapTarget> targets = [.. Targets().Select(dimension =>
            {
                Profile profile = Scene.GetDrawingProfile(world, dimension, out ScanType scanType);
                return new WebMapTarget(dimension, profile, scanType, Describe(world, dimension), options.IncludeMarkers ? Markers(world.Level, dimension) : []);
            })];
            string title = world.Level.WorldName ?? "Map";
            string current = WebMapFolder.IdOf(world.CurrentDimension.Dimension);
            AssetPack assetPack = Scene.Domain.DefaultAssetPack;

            return (progress, cancellation) => _ = Export(assetPack, world.Level, targets, folder, title, current, progress, cancellation);
        }

        // Writes the dimensions into the folder as one web map that opens on the current one, and returns those
        // written (a dimension with no regions is left out). Runs anywhere
        public static IList<WebMapDimension> Export(AssetPack defaultAssetPack, Level level, IList<WebMapTarget> targets, string folder, string title, string current,
            IProgress<WebMapProgress>? progress, CancellationToken cancellation)
        {
            WebMapFolder.Clear(folder, targets.Select(t => WebMapFolder.IdOf(t.Dimension)));

            List<WebMapDimension> dimensions = [];
            foreach (WebMapTarget target in targets)
            {
                cancellation.ThrowIfCancellationRequested();

                ChunkMapperPack pack = new(defaultAssetPack);
                pack.UpdatePack(target.Profile, target.ScanType);

                SceneInfo scene = new(level, target.Dimension);
                if (WebMapExporter.Export(pack, scene, folder, WebMapFolder.IdOf(target.Dimension), target.Dimension.Name, target.Subtitle, target.Markers, progress, cancellation)
                    is WebMapDimension dimension) dimensions.Add(dimension);
            }

            WebMapPage.Write(folder, title, dimensions, current);
            progress?.Report(new WebMapProgress(WebMapStage.Done, 1, 1));
            return dimensions;
        }

        public string Describe()
        {
            if (Scene.Domain.CurrentWorld is not WorldDomain world) return string.Empty;

            // "Overworld, Nether and End · Default · Nether slice at Y 30"
            List<string> parts = ["Overworld, Nether and End", world.Style.Metadata.Name];
            foreach (DimensionDomain dimension in world.Dimensions)
            {
                if (dimension.Cut is HeightCut cut) parts.Add($"{dimension.Dimension.Name} {Describe(cut)}");
            }
            return string.Join(" · ", parts);
        }

        public static string Describe(HeightCut cut) => cut.Caves ? $"caves below Y {cut.Y}" : $"slice at Y {cut.Y}";

        // The three, and the dimension on screen if it's another
        private IEnumerable<Dimension> Targets()
        {
            List<Dimension> dimensions = [.. WebMapFolder.Dimensions.Select(d => d.Dimension)];
            if (Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension current && !dimensions.Contains(current)) dimensions.Add(current);
            return dimensions;
        }

        // "Default · slice at Y 30"
        private static string Describe(WorldDomain world, Dimension dimension)
        {
            List<string> parts = [world.Style.Metadata.Name];
            if (world.GetDimension(dimension)?.Cut is HeightCut cut) parts.Add(Describe(cut));
            return string.Join(" · ", parts);
        }

        // The world spawn in the Overworld and the player in whichever dimension they are in
        public static List<WebMapMarker> Markers(Level level, Dimension dimension)
        {
            List<WebMapMarker> markers = [];
            if (dimension == Dimension.Overworld) markers.Add(new WebMapMarker("Spawn", level.WorldGen.WorldSpawn.X, level.WorldGen.WorldSpawn.Z));
            if (level.Player.Dimension == dimension) markers.Add(new WebMapMarker("Player", level.Player.Position.X, level.Player.Position.Z));
            return markers;
        }
    }

    // One dimension of a web map and how it's drawn
    public record WebMapTarget(Dimension Dimension, Profile Profile, ScanType ScanType, string Subtitle, IList<WebMapMarker> Markers);
}
