using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WorldEditor;
#if AVALONIA
using Avalonia.Threading;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Controller
{
    // The Live button: while it's on, the dimension on show is checked every couple of seconds for what the game has
    // saved, and the regions that changed are drawn again. A check is only a folder listing; the world is read once
    // its files have been still for a whole check, so a save in progress isn't read halfway. A .mcworld is a copy that
    // never changes, so there's nothing to watch there. Other dimensions catch up when they're switched to
    public class LiveReloadTool : ToggleableTool
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
        // A game in full swing may never leave its files still for a whole check; after this many checks of
        // changing files they're read anyway. Bedrock's log only grows and a half-written record at its end is
        // skipped; a Java region file caught mid-write loses a chunk or two until the next look
        private const int MaxChecksWaiting = 5;

        public Scene Scene { get; }
        public WorldOpener Opener { get; }
        public AppSettings Settings { get; }

        // For the info bar: what live reload last did, or null when it isn't watching
        public string? Note { get; private set; }
        public event EventHandler? NoteChanged;

        private class Watch
        {
            public string Seen = string.Empty;
            public bool Changed = true;
            public int Waiting;
        }

        // Per store, so each dimension keeps its own; only touched from the checking thread
        private readonly Dictionary<IRefreshableRegionStore, Watch> _watches = [];
        private Watch _levelWatch = new();
        private WorldDomain? _world;

        public LiveReloadTool(Scene scene, WorldOpener opener, AppSettings settings)
        {
            Scene = scene;
            Opener = opener;
            Settings = settings;

            Enabled = false;
            IsTurnedOn = settings.LiveReload;
            OnTurnedOn += isTurnedOn =>
            {
                Settings.LiveReload = isTurnedOn;
                Settings.Save();
                SetNote(Watching ? "Live" : null);
            };
            Scene.WorldChanged += (old, current) =>
            {
                Enabled = CanWatch(current);
                SetNote(Watching ? "Live" : null);
            };

            _ = Task.Run(Run);
        }

        private bool Watching => IsTurnedOn && Enabled;

        // A world opened from a folder, whose regions the store can look at again
        private bool CanWatch(WorldDomain world)
        {
            string source = Opener.SourceOf(world.Level) ?? world.Level.Directory ?? string.Empty;
            return !McWorldArchive.IsArchive(source) && world.CurrentDimension.Scene.SceneParameter.RegionStore is IRefreshableRegionStore;
        }

        private async Task Run()
        {
            while (true)
            {
                await Task.Delay(Interval);
                try
                {
                    Check();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    SetNote("Live: can't read the world just now (the game may be holding it); trying again");
                    Console.WriteLine($"Live reload couldn't read the world: {e.Message}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Live reload: {e}");
                }
            }
        }

        private void Check()
        {
            if (!Watching || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            if (world != _world)
            {
                _world = world;
                _watches.Clear();
                _levelWatch = new Watch() { Seen = LevelStamp(world.Level), Changed = false };
            }

            DimensionDomain dimension = world.CurrentDimension;
            if (dimension.Scene.SceneParameter.RegionStore is not IRefreshableRegionStore store) return;
            if (store is IBackgroundRegionStore { Ready.IsCompleted: false }) return;

            // A store seen for the first time (a dimension just switched to) gets one look straight away
            if (!_watches.TryGetValue(store, out Watch? watch)) _watches[store] = watch = new Watch();

            if (Settled(watch, store.Stamp()))
            {
                RegionChanges? changes = store.Refresh();
                watch.Changed = false;

                // Drawn only if the map still shows what was checked
                if (changes is not null && Scene.Domain.CurrentWorld == world && world.CurrentDimension == dimension)
                {
                    Scene.RegionLoader.Refresh(changes.Regions);
                    SetNote(changes.Chunks == 1 ? $"Live: 1 chunk updated at {DateTime.Now:HH:mm}" : $"Live: {changes.Chunks:N0} chunks updated at {DateTime.Now:HH:mm}");
                }
                else if (Note?.StartsWith("Live: can't") == true)
                {
                    SetNote("Live");
                }
            }

            if (Settled(_levelWatch, LevelStamp(world.Level)))
            {
                _levelWatch.Changed = false;
                ReadLevel(world.Level);
            }
        }

        // Whether a look is due: something changed earlier and the files have stayed as they are since the last check,
        // or have kept changing for too long to wait for
        private static bool Settled(Watch watch, string stamp)
        {
            if (stamp != watch.Seen)
            {
                watch.Seen = stamp;
                watch.Changed = true;
                if (++watch.Waiting < MaxChecksWaiting) return false;
            }
            if (!watch.Changed) return false;
            watch.Waiting = 0;
            return true;
        }

        private static string LevelStamp(Level level)
        {
            FileInfo file = new(Path.Combine(level.Directory ?? string.Empty, "level.dat"));
            return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : string.Empty;
        }

        // The player's place and the spawn point move as the game is played; the rest of the world's settings stay
        private static void ReadLevel(Level level)
        {
            if (level.Directory is null || WorldLocation.Read(level.Directory) is not Level fresh) return;
            level.Player = fresh.Player;
            level.WorldGen = fresh.WorldGen;
            level.LastPlayed = fresh.LastPlayed;
        }

        private void SetNote(string? note)
        {
            if (note == Note) return;
            Note = note;
#if AVALONIA
            Dispatcher.UIThread.Post(() => NoteChanged?.Invoke(this, EventArgs.Empty));
#else
            Application.Current?.Dispatcher.BeginInvoke(() => NoteChanged?.Invoke(this, EventArgs.Empty));
#endif
        }
    }
}
