using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    // Exports the world's Overworld, Nether and End, those it has, as one zoomable web map, each drawn as the
    // view draws it: same style, and for a dimension with the height panel on, the same height cut. The settings
    // are taken when the export starts, so changing the view meanwhile doesn't change the export. Into a folder
    // holding an earlier export of the same world drawn the same way, only the regions the game has saved since are
    // drawn again
    public class WebMapSaver
    {
        public Scene Scene { get; }

        // The world's markup, which the page can show
        public Func<MarkupDocument?>? Markup { get; set; }

        public WebMapSaver(Scene scene)
        {
            Scene = scene;
        }

        public bool CanSave => Scene.Domain.CurrentWorld is not null;

        // What an export into the folder writes over; empty when the folder is empty or holds nothing it would touch,
        // or when it holds an earlier export of this world, which the export brings up to date
        public IList<string> WillReplace(string folder)
        {
            if (Scene.Domain.CurrentWorld is WorldDomain world && Targets().Any(d => WebMapManifest.Read(folder, WebMapFolder.IdOf(d))?.World == WorldKey(world.Level))) return [];
            return WebMapFolder.WillReplace(folder, Targets().Select(WebMapFolder.IdOf));
        }

        // Call on the UI thread; the returned export can run anywhere
        public Action<IProgress<WebMapProgress>?, CancellationToken> Prepare(string folder, WebMapOptions options)
        {
            WorldDomain world = Scene.Domain.CurrentWorld ?? throw new InvalidOperationException("No world is open");

            List<WebMapTarget> targets = [.. Targets().Select(dimension =>
            {
                Profile profile = Scene.GetDrawingProfile(world, dimension, out ScanType scanType);
                return new WebMapTarget(dimension, profile, scanType, Describe(world, dimension), options.IncludeMarkers ? Markers(world.Level, dimension) : [],
                    DrawingKey(world.Style, profile, scanType, world.Level));
            })];
            string title = world.Level.WorldName ?? "Map";
            string current = WebMapFolder.IdOf(world.CurrentDimension.Dimension);
            AssetPack assetPack = Scene.Domain.DefaultAssetPack;
            MarkupDocument? markup = options.IncludeMarkup ? Markup?.Invoke()?.Clone() : null;

            bool redrawAll = options.RedrawAll;

            return (progress, cancellation) => _ = Export(assetPack, world.Level, targets, folder, title, current, progress, cancellation, markup, redrawAll);
        }

        // Writes the dimensions into the folder as one web map that opens on the current one, and returns those
        // written (a dimension with no regions is left out). Runs anywhere
        public static IList<WebMapDimension> Export(AssetPack defaultAssetPack, Level level, IList<WebMapTarget> targets, string folder, string title, string current,
            IProgress<WebMapProgress>? progress, CancellationToken cancellation, MarkupDocument? markup = null, bool redrawAll = false)
        {
            // Each dimension's own folder is cleared, or kept for an update, by the exporter
            WebMapFolder.Clear(folder, []);

            List<WebMapDimension> dimensions = [];
            foreach (WebMapTarget target in targets)
            {
                cancellation.ThrowIfCancellationRequested();

                ChunkMapperPack pack = new(defaultAssetPack);
                pack.UpdatePack(target.Profile, target.ScanType);

                SceneInfo scene = new(level, target.Dimension);
                WebMapIdentity? identity = target.Drawing is string drawing ? new WebMapIdentity(WorldKey(level), drawing) : null;
                if (WebMapExporter.Export(pack, scene, folder, WebMapFolder.IdOf(target.Dimension), target.Dimension.Name, target.Subtitle, target.Markers,
                    progress, cancellation, identity, redrawAll) is WebMapDimension dimension) dimensions.Add(dimension);
            }

            WebMapPage.Write(folder, title, dimensions, current, markup);
            progress?.Report(new WebMapProgress(WebMapStage.Done, 1, 1));
            return dimensions;
        }

        // "Overworld: 3 of 26 regions changed, Nether: none of 6 changed"; empty when every dimension was drawn whole
        public static string DescribeUpdate(IList<WebMapDimension> dimensions)
        {
            if (!dimensions.Any(d => d.Updated)) return string.Empty;

            return string.Join(", ", dimensions.Select(d => d.Redrawn switch
            {
                0 => $"{d.Name}: none of {d.Total} changed",
                _ when d.Redrawn == d.Total => $"{d.Name}: all {d.Total} drawn",
                _ => $"{d.Name}: {d.Redrawn} of {d.Total} region{(d.Total == 1 ? "" : "s")} changed"
            }));
        }

        // The same world even when copied, moved or opened from a newer .mcworld
        public static string WorldKey(Level level) => $"{level.Edition}|{level.WorldName}|{level.WorldGen.Seed}";

        // Everything that changes how a dimension is drawn, folded into one string: the build of Mapee, the style and
        // whether the block filter changed its assets, the scan, height and light settings, and the world's data
        // packs, whose biome colours the style may use
        public static string DrawingKey(Style style, Profile profile, ScanType scanType, Level level)
        {
            HeightmapSettings heightmap = profile.HeightmapSettings;
            RenderSettings render = profile.RenderSettings;
            StringBuilder key = new();
            key.Append(typeof(WebMapWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion).Append('|')
                .Append(style.Metadata.Id).Append('|').Append(style.AssetRevision).Append('|').Append(scanType).Append('|')
                .Append(heightmap.HeightmapType).Append(',').Append(heightmap.SetY).Append(',').Append(heightmap.NbtHeightmap).Append('|')
                .Append(FormattableString.Invariant($"{render.SkyLightIntensity},{render.AmbientLightIntensity},{render.AltitudeYOffset},{render.SemiTransparentStepIntensity}"));

            if (!style.Metadata.DisableDatapackStyle)
            {
                // A pack is a zip or a folder
                foreach (string dataPack in level.DataPacks)
                {
                    string path = Path.Combine(level.Directory ?? "", "datapacks", Path.GetFileName(dataPack));
                    IEnumerable<FileInfo> files = Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
                        : File.Exists(path) ? [new FileInfo(path)] : [];
                    key.Append('|').Append(Path.GetFileName(path)).Append(':').Append(files.Sum(f => f.Length)).Append(':')
                        .Append(files.Select(f => f.LastWriteTimeUtc.Ticks).DefaultIfEmpty().Max());
                }
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())), 0, 16);
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
    // Drawing is DrawingKey's; without it the dimension is always drawn whole
    public record WebMapTarget(Dimension Dimension, Profile Profile, ScanType ScanType, string Subtitle, IList<WebMapMarker> Markers, string? Drawing = null);
}
