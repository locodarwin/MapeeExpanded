using CommonUtilities.Collections.Synchronized;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WorldEditor;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for WorldBrowser.xaml
    /// </summary>
    public partial class WorldBrowser : Window
    {
        public IList<string> BaseDirectories { get; private set; }
        public IObjectReader<string, Level?> LevelReader { get; private set; }

        public WorldPanelEntry Selected { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        public event EventHandler? WorldSelected;

        private readonly IEnumerable<WorldPanelEntry> _entries;
        private readonly BitmapImage _default = new BitmapImage(new Uri("/Resources/Image/Misc/DefaultWorld_128px.png", UriKind.Relative));

        private bool _wasDown = false;

        // recentWorlds, most recent first, are listed ahead of the worlds found in baseDirectories
        public WorldBrowser(IList<string> baseDirectories, IObjectReader<string, Level?> levelReader, IList<string>? recentWorlds = null)
        {
            InitializeComponent();

            BaseDirectories = baseDirectories;
            LevelReader = levelReader;

            StartupLog.Mark($"World browser looks in {string.Join(", ", baseDirectories)}");
            List<WorldPanelEntry> recent = LoadRecent(recentWorlds ?? []);
            StartupLog.Mark($"  {recent.Count} recent worlds read");
            HashSet<string> listed = recent.Select(x => WorldLocation.Key(x.Level.Directory ?? x.Source)).ToHashSet();

            _entries = recent.Concat(BaseDirectories.SelectMany(LoadEntries)
                .Where(x => !listed.Contains(WorldLocation.Key(x.Level.Directory ?? x.Source)))
                .OrderByDescending(x => x.Level.LastPlayed)).ToList();
            StartupLog.Mark($"  {_entries.Count()} worlds listed");
            foreach (WorldPanelEntry entry in _entries)
            {
                AddWorld(entry);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = new CustomFrameWindowInitializer(this, Template);
        }

        private IEnumerable<WorldPanelEntry> LoadEntries(string baseDirectory)
        {
            if(!Directory.Exists(baseDirectory)) return Enumerable.Empty<WorldPanelEntry>();

            string[] worlds = Directory.GetDirectories(baseDirectory);
            SynchronizedList<WorldPanelEntry> output = new(worlds.Length);

            Parallel.ForEach(worlds, world =>
            {
                if (!TryLoadEntry(world, world, out WorldPanelEntry entry)) return;
                output.Add(entry);
            });

            return output.OrderByDescending(x => x.Level.LastPlayed);
        }
        // Worlds that have since been moved or deleted are left out
        private List<WorldPanelEntry> LoadRecent(IList<string> sources)
        {
            WorldPanelEntry?[] output = new WorldPanelEntry?[sources.Count];
            Parallel.For(0, sources.Count, i =>
            {
                try
                {
                    string? world = WorldLocation.FindDirectory(sources[i]);
                    if (world is not null && TryLoadEntry(world, sources[i], out WorldPanelEntry entry)) output[i] = entry;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            });
            return [.. output.OfType<WorldPanelEntry>()];
        }
        private bool TryLoadEntry(string world, string source, out WorldPanelEntry entry)
        {
            StartupLog.Mark($"  reading {world}");
            try
            {
                Level? level = LevelReader.Read($"{world}\\level.dat");
                if (level is null) 
                {
                    entry = new WorldPanelEntry();
                    return false;
                }
                
                BitmapImage icon;

                // Bedrock worlds keep their thumbnail in world_icon.jpeg
                string iconFile = $"{world}\\icon.png";
                if (!File.Exists(iconFile)) iconFile = $"{world}\\world_icon.jpeg";

                if (File.Exists(iconFile))
                {
                    using (FileStream imageStream = File.OpenRead(iconFile))
                    {
                        icon = new BitmapImage();
                        icon.BeginInit();
                        icon.StreamSource = imageStream;
                        icon.CacheOption = BitmapCacheOption.OnLoad;
                        icon.EndInit();
                        icon.Freeze();
                    }
                }
                else
                {
                    icon = _default;
                }

                entry = new WorldPanelEntry(level, icon, source);
                StartupLog.Mark($"  read {world}");
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

            panel.MouseDown += PanelMouseDown;
            panel.MouseUp += PanelMouseUp;

            if (WorldPanelContainer.Children.Count < 1)
            {
                panel.Height += 4;
            }

            WorldPanelContainer.RowDefinitions.Add(new RowDefinition()
            {
                Height = new GridLength(panel.Height)
            });

            Grid.SetRow(panel, WorldPanelContainer.RowDefinitions.Count - 1);
            Grid.SetColumn(panel, 0);
            WorldPanelContainer.Children.Add(panel);
        }

        private void PanelMouseDown(object sender, EventArgs e)
        {
            _wasDown = true;
        }
        private void PanelMouseUp(object sender, EventArgs e)
        {
            if (!_wasDown) return;

            Selected = ((WorldPanel)sender).Entry;
            WorldSelected?.Invoke(this, EventArgs.Empty);

            DialogClosed = false;
            Close();
        }

        private static string? ExtractArchive(string file)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                return McWorldArchive.Extract(file);
            }
            catch (Exception e)
            {
                MessageBox.Show($"Couldn't open {Path.GetFileName(file)}: {e.Message}", "Mapee", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new()
            {
                Filter = "Minecraft worlds (level.dat, .mcworld)|*.dat;*.mcworld"
            };
            bool? result = dialog.ShowDialog();
            if (result != true) return;

            string? directory = McWorldArchive.IsArchive(dialog.FileName) ? ExtractArchive(dialog.FileName) : Path.GetDirectoryName(dialog.FileName);
            if (directory is null || !TryLoadEntry(directory, McWorldArchive.IsArchive(dialog.FileName) ? dialog.FileName : directory, out WorldPanelEntry entry)) return;

            Selected = entry;
            WorldSelected?.Invoke(this, EventArgs.Empty);

            DialogClosed = false;
            Close();
        }
    }
}
