using MapScanner;
using WorldEditor;

namespace Mapper
{
    // Renders every region of a dimension, as the map view would, into a web map, with the height of each block
    // drawn. It takes its own mapper pack and scene so the view can keep drawing while it runs. Regions are drawn
    // in WebMapGroups, so neighbours in a group are shaded against each other, as they are on screen. Given an
    // identity, it updates an earlier export of the same world drawn the same way: only groups where the game
    // saved a chunk since are drawn again
    public static class WebMapExporter
    {
        private const int RegionsInParallel = WebMapGroups.Width;
        private const int ChunksInParallel = 8;

        // Writes under <folder>/<id>. Nothing is written for a dimension with no regions; it returns null instead.
        // Without an identity, or with redrawAll, everything is drawn
        public static WebMapDimension? Export(MapperPack mapperPack, SceneInfo scene, string folder, string id, string name,
            string subtitle, IList<WebMapMarker> markers, IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default,
            WebMapIdentity? identity = null, bool redrawAll = false)
        {
            if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);

            Coords[] regions = [.. scene.Regions];
            if (regions.Length == 0)
            {
                WebMapFolder.ClearDimension(folder, id);
                return null;
            }
            List<Coords[]> groups = WebMapGroups.Group(regions);
            static string NameOf(Coords[] group) => WebMapGroups.Name(WebMapGroups.Of(group[0]));

            // Fingerprints only when the store can say what was saved; a store that can't is drawn whole each time
            IReadOnlyDictionary<Coords, ulong>? stamps = identity is not null && scene.RegionStore is IChunkStampRegionStore stampStore ? stampStore.ChunkStamps() : null;
            Dictionary<string, string> fingerprints = stamps is null ? []
                : groups.AsParallel().ToDictionary(NameOf, g => WebMapGroups.Fingerprint(g, stamps));

            WebMapManifest? previous = !redrawAll && identity is not null && stamps is not null
                && WebMapManifest.Read(folder, id) is WebMapManifest earlier && earlier.Matches(identity) ? earlier : null;
            if (previous is null) WebMapFolder.ClearDimension(folder, id);
            else WebMapManifest.Delete(folder, id);

            WebMapWriter writer = new(folder, id);
            List<Coords[]> draw = [.. groups.Where(g => previous is null || previous.Groups.GetValueOrDefault(NameOf(g)) != fingerprints[NameOf(g)])];
            HashSet<Coords> drawn = [.. draw.SelectMany(g => g)];

            // What the earlier export drew in groups drawn again or gone from the world goes; the rest is kept
            HashSet<Coords>? changed = null;
            if (previous is not null)
            {
                HashSet<Coords> redrawnGroups = [.. draw.Select(g => WebMapGroups.Of(g[0]))];
                HashSet<Coords> present = [.. groups.Select(g => WebMapGroups.Of(g[0]))];
                changed = [.. drawn];

                foreach (Coords region in previous.DrawnRegions())
                {
                    Coords group = WebMapGroups.Of(region);
                    if (present.Contains(group) && !redrawnGroups.Contains(group)) writer.Keep([region]);
                    else
                    {
                        writer.Forget(region);
                        changed.Add(region);
                    }
                }
            }

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => scene.RegionStore,
                new ChunkEnumeratorFromRegionFactory(index => mapperPack.ChunkReader));
            HeightsEnumerationBody body = new(mapperPack, RegionsInParallel, ChunksInParallel)
            {
                RegionScanned = (coords, chunks) => writer.AddHeights(coords, Heights(coords, chunks)),
                RegionRendered = (coords, canvas) =>
                {
                    using (canvas)
                    {
                        if (canvas.GetPixels() is not byte[] pixels) return;

                        int left = canvas.TopLeftPoint.X - (canvas.PixelWidth - 1);
                        writer.AddRegion(coords, left, canvas.TopLeftPoint.Z, canvas.PixelWidth, canvas.PixelHeight, pixels);
                    }
                }
            };

            int total = drawn.Count, done = 0;
            progress?.Report(new WebMapProgress(WebMapStage.Rendering, 0, total, name, previous is not null));
            foreach (Coords[] group in draw)
            {
                cancellation.ThrowIfCancellationRequested();

                enumerator.Enumerate(group, body);
                done += group.Length;
                progress?.Report(new WebMapProgress(WebMapStage.Rendering, done, total, name, previous is not null));
            }

            WebMapDimension finished = writer.Finish(name, subtitle, markers, progress, cancellation, changed);
            WebMapDimension dimension = finished with { Redrawn = finished.Regions.Count(drawn.Contains), Total = finished.Regions.Count, Updated = previous is not null };

            if (identity is not null && stamps is not null)
            {
                new WebMapManifest
                {
                    Format = WebMapManifest.CurrentFormat,
                    World = identity.World,
                    Drawing = identity.Drawing,
                    Groups = fingerprints,
                    Regions = [.. dimension.Regions.OrderBy(c => c.Z).ThenBy(c => c.X).SelectMany(c => new[] { c.X, c.Z })]
                }.Write(folder, id);
            }
            return dimension;
        }

        // The top block the scan kept in each column, which is the one drawn there
        public static short[] Heights(Coords region, IEnumerable<IScannedChunk> chunks)
        {
            short[] heights = new short[WebMapWriter.TileSize * WebMapWriter.TileSize];
            Array.Fill(heights, short.MinValue);

            foreach (IScannedChunk chunk in chunks)
            {
                if (MathUtilities.FindSectionY(chunk.Coords.X, 32) != region.X || MathUtilities.FindSectionY(chunk.Coords.Z, 32) != region.Z) continue;

                int left = MathUtilities.NegMod(chunk.Coords.X, 32) * 16, top = MathUtilities.NegMod(chunk.Coords.Z, 32) * 16;
                for (int i = 0; i < 256; i++)
                {
                    ScannedColumn column = chunk.GetColumn(i);
                    short y = column.BlockSpans.Length > 0 ? column.BlockSpans.Span[0].TopY : column.BottomBlock.FirstInstanceY;
                    heights[(top + i / 16) * WebMapWriter.TileSize + left + i % 16] = y;
                }
            }

            return heights;
        }

        // Hands over each region's scanned chunks before they're drawn
        private class HeightsEnumerationBody(MapperPack mapperPack, int regionsPerTask, int tasksPerRegion)
            : WorldMapperEnumerationBody(mapperPack, regionsPerTask, tasksPerRegion)
        {
            public Action<Coords, IList<IScannedChunk>>? RegionScanned { get; init; }

            protected override void Render(ScannedRegion[] regions, IStepProvider stepProvider)
            {
                Parallel.ForEach(regions, region => RegionScanned?.Invoke(region.Coords, region.Chunks));
                base.Render(regions, stepProvider);
            }
        }
    }
}
