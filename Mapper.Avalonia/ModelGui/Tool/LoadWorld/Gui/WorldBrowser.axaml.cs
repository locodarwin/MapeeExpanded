using CommonUtilities.Collections.Synchronized;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using WorldEditor;

namespace Mapper.Gui
{
    public partial class WorldBrowser : Window
    {
        public IList<string> BaseDirectories { get; private set; }
        public IObjectReader<string, Level?> LevelReader { get; private set; }

        public WorldPanelEntry Selected { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        public event EventHandler? WorldSelected;

        private readonly IEnumerable<WorldPanelEntry> _entries;
        private readonly Bitmap _default = AppImages.Load("/Resources/Image/Misc/DefaultWorld_128px.png");

        private bool _wasDown = false;

        public WorldBrowser(IList<string> baseDirectories, IObjectReader<string, Level?> levelReader)
        {
            InitializeComponent();

            BaseDirectories = baseDirectories;
            LevelReader = levelReader;

            _entries = BaseDirectories.SelectMany(LoadEntries).OrderByDescending(x => x.Level.LastPlayed).ToList();
            foreach (WorldPanelEntry entry in _entries)
            {
                AddWorld(entry);
            }

            if (!_entries.Any())
            {
                EmptyLabel.Text = "No worlds found in the usual places. Use Browse to open a world's level.dat or a .mcworld file.";
                EmptyLabel.IsVisible = true;
            }
        }

        private IEnumerable<WorldPanelEntry> LoadEntries(string baseDirectory)
        {
            if(!Directory.Exists(baseDirectory)) return Enumerable.Empty<WorldPanelEntry>();

            string[] worlds = Directory.GetDirectories(baseDirectory);
            SynchronizedList<WorldPanelEntry> output = new(worlds.Length);

            Parallel.ForEach(worlds, world =>
            {
                if (!TryLoadEntry(world, out WorldPanelEntry entry)) return;
                output.Add(entry);
            });

            return output.OrderByDescending(x => x.Level.LastPlayed);
        }
        private bool TryLoadEntry(string world, out WorldPanelEntry entry)
        {
            try
            {
                Level? level = LevelReader.Read(Path.Combine(world, "level.dat"));
                if (level is null) 
                {
                    entry = new WorldPanelEntry();
                    return false;
                }
                
                Bitmap icon;

                // Bedrock worlds keep their thumbnail in world_icon.jpeg
                string iconFile = Path.Combine(world, "icon.png");
                if (!File.Exists(iconFile)) iconFile = Path.Combine(world, "world_icon.jpeg");

                if (File.Exists(iconFile))
                {
                    using FileStream imageStream = File.OpenRead(iconFile);
                    icon = new Bitmap(imageStream);
                }
                else
                {
                    icon = _default;
                }

                entry = new WorldPanelEntry(level, icon);
                return true;
            }
            catch(Exception e)
            {
                Console.WriteLine(e.Message);

                entry = new WorldPanelEntry();
                return false;
            }
        }
        private void AddWorld(WorldPanelEntry entry)
        {
            WorldPanel panel = new(entry);

            panel.PointerPressed += PanelMouseDown;
            panel.PointerReleased += PanelMouseUp;

            if (WorldPanelContainer.Children.Count < 1)
            {
                panel.Height += 4;
            }

            WorldPanelContainer.Children.Add(panel);
        }

        private void PanelMouseDown(object? sender, PointerPressedEventArgs e)
        {
            _wasDown = true;
        }
        private void PanelMouseUp(object? sender, PointerReleasedEventArgs e)
        {
            if (!_wasDown || sender is not WorldPanel panel) return;

            Selected = panel.Entry;
            WorldSelected?.Invoke(this, EventArgs.Empty);

            DialogClosed = false;
            Close();
        }

        private async Task<string?> ExtractArchive(string file)
        {
            Cursor = new Cursor(StandardCursorType.Wait);
            try
            {
                return await Task.Run(() => McWorldArchive.Extract(file));
            }
            catch (Exception e)
            {
                await MessageWindow.Show($"Couldn't open {Path.GetFileName(file)}: {e.Message}", "Mapee", this);
                return null;
            }
            finally
            {
                Cursor = Cursor.Default;
            }
        }

        private async void Button_Click(object? sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                Title = "Open a world",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Minecraft worlds (level.dat, .mcworld)") { Patterns = ["*.dat", "*.mcworld"] }]
            });

            string? fileName = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (fileName is null) return;

            string? directory = McWorldArchive.IsArchive(fileName) ? await ExtractArchive(fileName) : Path.GetDirectoryName(fileName);
            if (directory is null || !TryLoadEntry(directory, out WorldPanelEntry entry)) return;

            Selected = entry;
            WorldSelected?.Invoke(this, EventArgs.Empty);

            DialogClosed = false;
            Close();
        }
    }
}
