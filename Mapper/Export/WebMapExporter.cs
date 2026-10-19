using WorldEditor;

namespace Mapper
{
    // Renders every region of a dimension, as the map view would, into a web map. It takes its own mapper
    // pack and scene so the view can keep drawing while it runs. Regions go in rows of four so neighbours
    // in a row are shaded against each other, as they are on screen
    public static class WebMapExporter
    {
        private const int RegionsInParallel = 4;
        private const int ChunksInParallel = 8;

        public static void Export(MapperPack mapperPack, SceneInfo scene, WebMapWriter writer, WebMapInfo info,
            IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default)
        {
            if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);

            Coords[] regions = [.. scene.Regions.OrderBy(c => c.Z).ThenBy(c => c.X)];

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => scene.RegionStore,
                new ChunkEnumeratorFromRegionFactory(index => mapperPack.ChunkReader));
            WorldMapperEnumerationBody body = new(mapperPack, RegionsInParallel, ChunksInParallel)
            {
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

            progress?.Report(new WebMapProgress(WebMapStage.Rendering, 0, regions.Length));
            for (int i = 0; i < regions.Length; i += RegionsInParallel)
            {
                cancellation.ThrowIfCancellationRequested();

                enumerator.Enumerate(regions[i..Math.Min(i + RegionsInParallel, regions.Length)], body);
                progress?.Report(new WebMapProgress(WebMapStage.Rendering, Math.Min(i + RegionsInParallel, regions.Length), regions.Length));
            }

            writer.Finish(info, progress, cancellation);
        }
    }
}
