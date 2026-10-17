namespace WorldEditor
{
    // A region store that finds its regions in the background. Exists, Count and enumeration wait
    // for it, so the UI should check Ready first instead of reading them straight away
    public interface IBackgroundRegionStore : IRegionStore
    {
        Task Ready { get; }
    }
}
