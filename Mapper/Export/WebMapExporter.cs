using MapScanner;
using WorldEditor;

namespace Mapper
{
    // Renders every region of a dimension, as the map view would, into a web map, with the height of each block
    // drawn. It takes its own mapper pack and scene so the view can keep drawing while it runs. Regions go in rows
    // of four so neighbours in a row are shaded against each other, as they are on screen
    public static class WebMapExporter
    {
        private const int RegionsInParallel = 4;
        private const int ChunksInParallel = 8;

        // Writes under <folder>/<id>. Nothing is written for a dimension with no regions; it returns null instead
        public static WebMapDimension? Export(MapperPack mapperPack, SceneInfo scene, string folder, string id, string name,
            string subtitle, IList<WebMapMarker> markers, IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default)
        {
            if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);

            Coords[] regions = [.. scene.Regions.OrderBy(c => c.Z).ThenBy(c => c.X)];
            if (regions.Length == 0) return null;

            WebMapWriter writer = new(folder, id);

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

            progress?.Report(new WebMapProgress(WebMapStage.Rendering, 0, regions.Length, name));
            for (int i = 0; i < regions.Length; i += RegionsInParallel)
            {
                cancellation.ThrowIfCancellationRequested();

                enumerator.Enumerate(regions[i..Math.Min(i + RegionsInParallel, regions.Length)], body);
                progress?.Report(new WebMapProgress(WebMapStage.Rendering, Math.Min(i + RegionsInParallel, regions.Length), regions.Length, name));
            }

            return writer.Finish(name, subtitle, markers, progress, cancellation);
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
