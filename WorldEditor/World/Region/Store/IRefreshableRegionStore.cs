namespace WorldEditor
{
    // A store that can catch up with a world the game is still writing to. Stamp is cheap (a folder listing, nothing
    // read), so it can be asked every few seconds; Refresh does the reading, best once the stamp has stopped changing
    // so a save in progress isn't caught halfway
    public interface IRefreshableRegionStore : IRegionStore
    {
        string Stamp();

        // What changed since the store was opened or last refreshed; null if nothing did. Throws IOException or
        // UnauthorizedAccessException when the files can't be read, as when the game keeps them locked
        RegionChanges? Refresh();
    }

    // Regions: every region whose picture may have changed, including neighbours a changed chunk's light reaches.
    // Chunks: how many chunks were written
    public record RegionChanges(IReadOnlyCollection<Coords> Regions, int Chunks);
}
