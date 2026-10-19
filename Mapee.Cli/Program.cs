using Avalonia;
using Avalonia.Headless;
using Mapper;
using Mapper.Gui.Controller;
using Mapper.Gui.Logic;
using WorldEditor;

namespace Mapee.Cli
{
    // Draws a world the way Mapee does, without its window: one dimension into a PNG, or every dimension into a web
    // map. Exit codes: 0 done, 1 failed, 2 wrong usage, 130 cancelled (Ctrl+C)
    public static class Program
    {
        private const string Usage = """
            Usage:
              mapee-cli render <world> --out <map.png> [options]
                  Draws one dimension into a PNG, a pixel per block.
                  --dimension <d>          overworld (default), nether, end
                  --area <x1>,<z1>,<x2>,<z2>  only the blocks between these corners
              mapee-cli webmap <world> --out <folder> [options]
                  Exports a zoomable web map (open index.html), every dimension the world has.
                  --dimension <d>[,<d>]    only these dimensions
                  --no-markers             leave out the spawn and player markers
                  --force                  replace files in a folder that isn't an earlier web map
              mapee-cli worlds             lists the worlds in the game's world folders
              mapee-cli styles             lists the styles

            Options for render and webmap:
              --style <id or name>         how the map is coloured (default: Default; see mapee-cli styles)
              --slice <y>                  draw the world cut at height y, as the height panel's Slice
              --caves <y>                  draw the caves below height y, as the height panel's Caves

            <world> is a world folder, its level.dat, a .mcworld file, or the name of a world in the
            game's world folders (Java or Bedrock).
            """;

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            if (args[0] is "help" or "-h" or "--help" or "/?")
            {
                Console.WriteLine(Usage);
                return 0;
            }
            if (args[0] is "version" or "--version")
            {
                Console.WriteLine($"Mapee Expanded {typeof(Controller).Assembly.GetName().Version?.ToString(3)}");
                return 0;
            }

            using CancellationTokenSource cancel = new();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cancel.Cancel();
            };

            try
            {
                CommandLine line = CommandLine.Parse(args);
                return line.Command switch
                {
                    "render" => Render(line, cancel.Token),
                    "webmap" => WebMap(line, cancel.Token),
                    "worlds" => Worlds(),
                    _ => Styles()
                };
            }
            catch (UsageException e)
            {
                Console.Error.WriteLine($"mapee-cli: {e.Message}");
                Console.Error.WriteLine("Run mapee-cli --help for how to use it.");
                return 2;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Cancelled");
                return 130;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"mapee-cli: {e.Message}");
                return 1;
            }
        }

        private static int Render(CommandLine line, CancellationToken cancellation)
        {
            Dimension dimension = line.Dimensions?.Count switch
            {
                null => Dimension.Overworld,
                1 => line.Dimensions[0],
                _ => throw new UsageException("render draws one dimension; give one to --dimension")
            };

            Level level = OpenWorld(line.World!);
            ProgramDomain domain = LoadDomain();
            Style style = WorldStyle(domain, level, line.Style);
            (Profile profile, ScanType scanType, string description) = Drawing(domain, style, dimension, line.Cut);

            ChunkMapperPack pack = new(domain.DefaultAssetPack);
            pack.UpdatePack(profile, scanType);

            ConsoleProgress progress = new();
            MapImageBounds? bounds = MapImageExporter.Export(pack, new SceneInfo(level, dimension), line.Out!, line.Area, progress, cancellation);
            progress.Finish();

            if (bounds is not MapImageBounds drawn)
            {
                Console.Error.WriteLine(line.Area is null ? $"{level.WorldName} has nothing in {The(dimension)}" : $"{level.WorldName} has nothing in that area of {The(dimension)}");
                return 1;
            }

            Console.WriteLine($"Wrote {line.Out}: {drawn.Width:N0} x {drawn.Height:N0} px, {level.WorldName}'s {dimension.Name} ({description}), " +
                $"blocks {drawn.X}, {drawn.Z} to {drawn.X + drawn.Width - 1}, {drawn.Z + drawn.Height - 1}");
            return 0;
        }

        // "the Overworld", "The End"
        private static string The(Dimension dimension) => dimension.Name.StartsWith("The ") ? dimension.Name : $"the {dimension.Name}";

        private static int WebMap(CommandLine line, CancellationToken cancellation)
        {
            IList<Dimension> dimensions = line.Dimensions ?? [.. WebMapFolder.Dimensions.Select(d => d.Dimension)];
            string folder = line.Out!;

            // An earlier web map is replaced without asking, so a map can be refreshed from a script
            IList<string> replaced = WebMapFolder.WillReplace(folder, dimensions.Select(WebMapFolder.IdOf));
            if (replaced.Count > 0 && !WebMapFolder.IsEarlierExport(folder) && !line.Force)
            {
                throw new UsageException($"{folder} already has {string.Join(", ", replaced)}, which the web map would replace; add --force to replace them");
            }

            Level level = OpenWorld(line.World!);
            ProgramDomain domain = LoadDomain();
            Style style = WorldStyle(domain, level, line.Style);

            List<WebMapTarget> targets = [.. dimensions.Select(dimension =>
            {
                (Profile profile, ScanType scanType, string description) = Drawing(domain, style, dimension, line.Cut);
                return new WebMapTarget(dimension, profile, scanType, description, line.Markers ? WebMapSaver.Markers(level, dimension) : []);
            })];

            // The page opens on the player's dimension
            string current = WebMapFolder.IdOf(dimensions.Contains(level.Player.Dimension) ? level.Player.Dimension : dimensions[0]);

            ConsoleProgress progress = new();
            IList<WebMapDimension> written = WebMapSaver.Export(domain.DefaultAssetPack, level, targets, folder, level.WorldName ?? "Map", current, progress, cancellation);
            progress.Finish();

            if (written.Count == 0)
            {
                Console.Error.WriteLine($"{level.WorldName} has nothing in {string.Join(", ", dimensions.Select(d => d.Name))}");
                return 1;
            }

            Console.WriteLine($"Wrote {Path.Combine(folder, "index.html")}: {level.WorldName}, " +
                string.Join(", ", written.Select(d => $"{d.Name} {d.Regions.Count} region{(d.Regions.Count == 1 ? "" : "s")}")) + $" ({targets[0].Subtitle})");
            return 0;
        }

        private static int Worlds()
        {
            List<(string Path, Level? Level)> worlds = [.. FindWorlds()];
            if (worlds.Count == 0)
            {
                Console.WriteLine("No worlds in the game's world folders:");
                foreach (string directory in AnyEditionLevelReader.DefaultWorldDirectories()) Console.WriteLine($"  {directory}");
                return 0;
            }

            foreach ((string path, Level? level) in worlds)
            {
                string about = level is null ? "can't be read" : $"{level.Edition}, {level.Version.VersionName}, last played {level.LastPlayed:yyyy-MM-dd}";
                Console.WriteLine($"{level?.WorldName ?? Path.GetFileName(path)}  ({about})");
                Console.WriteLine($"  {path}");
            }
            return 0;
        }

        private static int Styles()
        {
            ProgramDomain domain = LoadDomain();
            int width = domain.Styles.Max(s => s.Metadata.Id.Length);
            foreach (Style style in domain.Styles) Console.WriteLine($"{style.Metadata.Id.PadRight(width)}  {style.Metadata.Name}");
            return 0;
        }

        // The styles, block colours and colour maps, read from the Resources folder beside the program. Avalonia runs
        // headless so the styles' icons can load; nothing is shown
        private static ProgramDomain LoadDomain()
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            AppBuilder.Configure<Application>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            return new ProgramDomain();
        }

        private static Level OpenWorld(string world)
        {
            string path = WorldLocation.IsWorld(world) ? world : FindNamedWorld(world);
            Level level = WorldLocation.Read(path) ?? throw new InvalidDataException($"Couldn't read the world at {path}");
            Scene.DetectAlphaWorld(level);
            return level;
        }

        private static string FindNamedWorld(string name)
        {
            List<(string Path, Level? Level)> matches = [.. FindWorlds().Where(w =>
                string.Equals(Path.GetFileName(w.Path), name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.Level?.WorldName, name, StringComparison.OrdinalIgnoreCase))];

            return matches.Count switch
            {
                0 => throw new UsageException($"{name} isn't a world folder, level.dat or .mcworld, nor the name of a world in the game's folders (see mapee-cli worlds)"),
                1 => matches[0].Path,
                _ => throw new UsageException($"More than one world is called {name}; give its folder instead:{string.Concat(matches.Select(m => $"{Environment.NewLine}  {m.Path}"))}")
            };
        }

        private static IEnumerable<(string Path, Level? Level)> FindWorlds()
        {
            foreach (string directory in AnyEditionLevelReader.DefaultWorldDirectories().Where(Directory.Exists))
            {
                foreach (string folder in Directory.GetDirectories(directory).Where(WorldLocation.IsWorld).Order())
                {
                    Level? level;
                    try
                    {
                        level = WorldLocation.Read(folder);
                    }
                    catch (Exception)
                    {
                        level = null;
                    }
                    yield return (folder, level);
                }
            }
        }

        // The style as a world opened in the app gets it: a copy, with the world's data pack biome colours
        private static Style WorldStyle(ProgramDomain domain, Level level, string? name)
        {
            Style style = name is null ? domain.Styles[0] : domain.Styles.FirstOrDefault(s =>
                string.Equals(s.Metadata.Id, name, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Metadata.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new UsageException($"There's no style called {name} (see mapee-cli styles)");

            style = style.Clone();
            if (!style.Metadata.DisableDatapackStyle && Scene.ReadDataPacks(level, domain.TexturePack.AssetPack.Colormap) is DataPackAssetPack dataPack)
            {
                Scene.UseDataPackColors(style, dataPack);
            }
            return style;
        }

        // How a dimension is drawn, as the map view draws it with the style and the height panel set the same way
        private static (Profile Profile, ScanType ScanType, string Description) Drawing(ProgramDomain domain, Style style, Dimension dimension, HeightCut? cut)
        {
            ScanType scanType = style.ScanType;
            Profile profile = style.GetProfile(dimension);
            if (cut is not HeightCut height) return (profile, scanType, style.Metadata.Name);

            profile = Scene.Cut(profile, dimension, height, domain.Styles, ref scanType);
            return (profile, scanType, $"{style.Metadata.Name} · {WebMapSaver.Describe(height)}");
        }
    }

    // Shows how far an export is on one line that's rewritten, or when the output goes to a file, a line per stage
    internal class ConsoleProgress : IProgress<WebMapProgress>
    {
        private readonly bool _interactive = !Console.IsErrorRedirected;
        private readonly object _lock = new();
        private string? _lastStage;
        private int _lastLength;

        public void Report(WebMapProgress value)
        {
            if (value.Stage == WebMapStage.Done) return;

            string stage = $"{value.Dimension}: {(value.Stage == WebMapStage.Rendering ? "drawing regions" : "making zoom levels")}";
            lock (_lock)
            {
                if (_interactive)
                {
                    if (_lastStage is not null && _lastStage != stage)
                    {
                        Console.Error.WriteLine();
                        _lastLength = 0;
                    }

                    string text = $"{stage} {value.Done}/{value.Total}";
                    Console.Error.Write("\r" + text.PadRight(_lastLength));
                    _lastLength = text.Length;
                }
                else if (_lastStage != stage)
                {
                    Console.Error.WriteLine(stage);
                }
                _lastStage = stage;
            }
        }

        public void Finish()
        {
            lock (_lock)
            {
                if (_interactive && _lastStage is not null) Console.Error.WriteLine();
                _lastStage = null;
            }
        }
    }

    internal class UsageException(string message) : Exception(message);

    internal class CommandLine
    {
        public required string Command { get; init; }
        public string? World { get; private set; }
        public string? Out { get; private set; }
        public IList<Dimension>? Dimensions { get; private set; }
        public string? Style { get; private set; }
        public HeightCut? Cut { get; private set; }
        public BlockArea? Area { get; private set; }
        public bool Markers { get; private set; } = true;
        public bool Force { get; private set; }

        private static readonly string[] Commands = ["render", "webmap", "worlds", "styles"];

        // Paths are made full here, before the program moves to its own folder to read its resources
        public static CommandLine Parse(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            if (!Commands.Contains(command)) throw new UsageException($"{args[0]} isn't a command; the commands are {string.Join(", ", Commands)}");

            CommandLine line = new() { Command = command };
            bool drawing = command is "render" or "webmap";

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--") || arg == "--")
                {
                    if (!drawing || line.World is not null) throw new UsageException($"Didn't expect {arg}");
                    line.World = File.Exists(arg) || Directory.Exists(arg) ? Path.GetFullPath(arg) : arg;
                    continue;
                }

                string option = arg, value = "";
                int equals = arg.IndexOf('=');
                if (equals > 0)
                {
                    option = arg[..equals];
                    value = arg[(equals + 1)..];
                }
                else if (option is "--out" or "--dimension" or "--style" or "--slice" or "--caves" or "--area")
                {
                    if (i + 1 >= args.Length) throw new UsageException($"{option} needs a value");
                    value = args[++i];
                }

                if (!drawing) throw new UsageException($"{command} takes no options");
                switch (option)
                {
                    case "--out":
                        line.Out = Path.GetFullPath(value);
                        break;
                    case "--dimension":
                        line.Dimensions = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseDimension).Distinct()];
                        if (line.Dimensions.Count == 0) throw new UsageException("--dimension needs a dimension");
                        break;
                    case "--style":
                        line.Style = value;
                        break;
                    case "--slice":
                    case "--caves":
                        if (line.Cut is not null) throw new UsageException("Give --slice or --caves, not both");
                        if (!short.TryParse(value, out short y)) throw new UsageException($"{option} needs a height, such as 64, not {value}");
                        line.Cut = new HeightCut(y, option == "--caves");
                        break;
                    case "--area" when command == "render":
                        if (!BlockArea.TryParse(value, out BlockArea area)) throw new UsageException($"--area needs two corners as x1,z1,x2,z2, such as -500,-500,500,500, not {value}");
                        line.Area = area;
                        break;
                    case "--no-markers" when command == "webmap":
                        line.Markers = false;
                        break;
                    case "--force" when command == "webmap":
                        line.Force = true;
                        break;
                    default:
                        throw new UsageException($"{command} has no option {option}");
                }
            }

            if (drawing)
            {
                if (line.World is null) throw new UsageException($"{command} needs a world");
                if (line.Out is null) throw new UsageException($"{command} needs --out, {(command == "render" ? "the PNG to write" : "the folder to write the web map into")}");
                if (command == "render" && Directory.Exists(line.Out)) throw new UsageException($"{line.Out} is a folder; --out names the PNG to write");
                if (command == "webmap" && File.Exists(line.Out)) throw new UsageException($"{line.Out} is a file; --out names the folder to write the web map into");
            }
            return line;
        }

        private static Dimension ParseDimension(string text)
        {
            return text.ToLowerInvariant() switch
            {
                "overworld" or "minecraft:overworld" => Dimension.Overworld,
                "nether" or "the_nether" or "minecraft:nether" or "minecraft:the_nether" => Dimension.Nether,
                "end" or "the_end" or "theend" or "minecraft:the_end" => Dimension.TheEnd,
                _ => throw new UsageException($"{text} isn't a dimension; give overworld, nether or end")
            };
        }
    }
}
