using WorldEditor;

namespace Mapper
{
    // Renders a whole dimension, or the part inside an area, into one PNG at a pixel per block, drawn as the map
    // view draws it. It goes a row of regions at a time and writes the image as it goes, so only one row of
    // regions is held in memory. Regions in a row are drawn four at a time so neighbours are shaded against each
    // other, as in the web map export
    public static class MapImageExporter
    {
        private const int RegionsInParallel = 4;
        private const int ChunksInParallel = 8;
        private const int RegionSize = 512;

        // Writes the image and returns where it lies in block coordinates, or null without writing anything when
        // no region is in the dimension (or the area)
        public static MapImageBounds? Export(MapperPack mapperPack, SceneInfo scene, string file, BlockArea? area = null,
            IProgress<WebMapProgress>? progress = null, CancellationToken cancellation = default)
        {
            if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);

            Coords[] regions = [.. scene.Regions.Where(c => area is not BlockArea a || a.TouchesRegion(c)).OrderBy(c => c.Z).ThenBy(c => c.X)];
            if (regions.Length == 0) return null;

            MapImageBounds bounds = area is BlockArea clip
                ? new MapImageBounds(clip.MinX, clip.MinZ, clip.MaxX - clip.MinX + 1, clip.MaxZ - clip.MinZ + 1)
                : new MapImageBounds(regions.Min(c => c.X) * RegionSize, regions.Min(c => c.Z) * RegionSize,
                    (regions.Max(c => c.X) - regions.Min(c => c.X) + 1) * RegionSize, (regions.Max(c => c.Z) - regions.Min(c => c.Z) + 1) * RegionSize);
            if ((long)bounds.Width * 4 * RegionSize > Array.MaxLength) throw new InvalidOperationException($"The map is {bounds.Width:N0} blocks wide, too wide to draw; give an area");

            string directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
            Directory.CreateDirectory(directory);
            string partial = file + ".part";

            ChunkEnumerator enumerator = new(RegionsInParallel, ChunksInParallel, () => scene.RegionStore,
                new ChunkEnumeratorFromRegionFactory(index => mapperPack.ChunkReader));

            int done = 0;
            progress?.Report(new WebMapProgress(WebMapStage.Rendering, 0, regions.Length, scene.Dimension.Name));
            try
            {
                using (FileStream stream = File.Create(partial))
                using (PngRowWriter png = new(stream, bounds.Width, bounds.Height))
                {
                    int rowsWritten = 0;
                    byte[] band = [];
                    foreach (IGrouping<int, Coords> row in regions.GroupBy(c => c.Z))
                    {
                        // The rows of the image this row of regions covers
                        int bandTop = Math.Max(row.Key * RegionSize, bounds.Z), bandBottom = Math.Min((row.Key + 1) * RegionSize, bounds.Z + bounds.Height);
                        if (bandTop - bounds.Z > rowsWritten) WriteEmpty(png, bounds.Width, bandTop - bounds.Z - rowsWritten);

                        int bandHeight = bandBottom - bandTop;
                        if (band.Length < bounds.Width * 4 * bandHeight) band = new byte[bounds.Width * 4 * bandHeight];
                        else Array.Clear(band);

                        WorldMapperEnumerationBody body = new(mapperPack, RegionsInParallel, ChunksInParallel)
                        {
                            RegionRendered = (coords, canvas) =>
                            {
                                using (canvas)
                                {
                                    if (canvas.GetPixels() is not byte[] pixels) return;

                                    int left = canvas.TopLeftPoint.X - (canvas.PixelWidth - 1);
                                    Place(band, bounds, bandTop, bandHeight, coords, left, canvas.TopLeftPoint.Z, canvas.PixelWidth, canvas.PixelHeight, pixels);
                                }
                            }
                        };

                        Coords[] inRow = [.. row];
                        for (int i = 0; i < inRow.Length; i += RegionsInParallel)
                        {
                            cancellation.ThrowIfCancellationRequested();

                            Coords[] batch = inRow[i..Math.Min(i + RegionsInParallel, inRow.Length)];
                            enumerator.Enumerate(batch, body);
                            done += batch.Length;
                            progress?.Report(new WebMapProgress(WebMapStage.Rendering, done, regions.Length, scene.Dimension.Name));
                        }

                        for (int y = 0; y < bandHeight; y++) png.WriteRow(band.AsSpan(y * bounds.Width * 4, bounds.Width * 4));
                        rowsWritten = bandBottom - bounds.Z;
                    }
                    if (rowsWritten < bounds.Height) WriteEmpty(png, bounds.Width, bounds.Height - rowsWritten);
                }

                File.Move(partial, file, overwrite: true);
            }
            catch
            {
                File.Delete(partial);
                throw;
            }

            progress?.Report(new WebMapProgress(WebMapStage.Done, 1, 1, scene.Dimension.Name));
            return bounds;
        }

        // Copies the part of a region's drawing (BGRA, its top left at left, top) that falls in the band, as RGBA
        private static void Place(byte[] band, MapImageBounds bounds, int bandTop, int bandHeight, Coords region, int left, int top, int width, int height, byte[] bgra)
        {
            // A region's drawing starts at the region's corner; the canvas may report a corner a pixel off it
            int originX = region.X * RegionSize + MathUtilities.NegMod(left, RegionSize);
            int originZ = region.Z * RegionSize + MathUtilities.NegMod(top, RegionSize);

            for (int y = 0; y < height; y++)
            {
                int z = originZ + y;
                if (z < bandTop || z >= bandTop + bandHeight || MathUtilities.NegMod(top, RegionSize) + y >= RegionSize) continue;

                for (int x = 0; x < width && MathUtilities.NegMod(left, RegionSize) + x < RegionSize; x++)
                {
                    int blockX = originX + x;
                    if (blockX < bounds.X || blockX >= bounds.X + bounds.Width) continue;

                    int from = (y * width + x) * 4, to = ((z - bandTop) * bounds.Width + blockX - bounds.X) * 4;
                    if (bgra[from + 3] == 0) continue;

                    band[to] = bgra[from + 2];
                    band[to + 1] = bgra[from + 1];
                    band[to + 2] = bgra[from];
                    band[to + 3] = bgra[from + 3];
                }
            }
        }

        private static void WriteEmpty(PngRowWriter png, int width, int rows)
        {
            byte[] empty = new byte[width * 4];
            for (int i = 0; i < rows; i++) png.WriteRow(empty);
        }
    }

    // Where an exported image lies, in blocks: its top left (north-west) corner and size, a pixel per block
    public readonly record struct MapImageBounds(int X, int Z, int Width, int Height);

    // A rectangle of blocks, corners included, in either order
    public readonly record struct BlockArea
    {
        public int MinX { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxZ { get; }

        public BlockArea(int x1, int z1, int x2, int z2)
        {
            MinX = Math.Min(x1, x2);
            MinZ = Math.Min(z1, z2);
            MaxX = Math.Max(x1, x2);
            MaxZ = Math.Max(z1, z2);
        }

        public bool TouchesRegion(Coords region)
        {
            return region.X * 512 <= MaxX && (region.X + 1) * 512 > MinX && region.Z * 512 <= MaxZ && (region.Z + 1) * 512 > MinZ;
        }

        // "x1,z1,x2,z2"
        public static bool TryParse(string text, out BlockArea area)
        {
            area = default;
            string[] parts = text.Split(',');
            if (parts.Length != 4) return false;

            int[] values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return false;
            }

            area = new BlockArea(values[0], values[1], values[2], values[3]);
            return true;
        }
    }
}
