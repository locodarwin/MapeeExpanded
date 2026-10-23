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
                  --markup                 draw the pins and zones marked on the world in Mapee
              mapee-cli webmap <world> --out <folder> [options]
                  Exports a zoomable web map (open index.html), every dimension the world has.
                  --dimension <d>[,<d>]    only these dimensions
                  --no-markers             leave out the spawn and player markers, and the pins and zones
                  --force                  replace files in a folder that isn't an earlier web map
                  --full                   draw every region again; otherwise, over an earlier web map
                                           of the same world drawn the same way, only regions the game
                                           has saved since are drawn
              mapee-cli find <world> --block <name>[,<name>] [options]
                  Lists where blocks are, one line per block found (x y z, then what it is).
                  Names are as Java has them (diamond_ore, ancient_debris), for Bedrock worlds too,
                  or one of the groups: diamonds, ancient_debris, spawners, chests, emeralds, buried.
                  --dimension <d>          overworld (default), nether, end
                  --area <x1>,<z1>,<x2>,<z2>  only between these corners
                  --y <min>,<max>          only between these heights
                  --csv                    as comma-separated values with a header line
              mapee-cli worlds             lists the worlds in the game's world folders
              mapee-cli styles             lists the styles

            Options for render and webmap:
              --style <id or name>         how the map is coloured (default: Default; see mapee-cli styles)
              --slice <y>                  draw the world cut at height y, as the height panel's Slice
              --caves <y>                  draw the caves below height y, as the height panel's Caves
              --markup <file.json>         the pins and zones in a file exported from Mapee, in place of
                                           those marked on the world (the web map shows the world's anyway)

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
                    "find" => Find(line, cancel.Token),
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

            (Level level, string source) = OpenWorld(line.World!);
            MarkupDocument? markup = line.Markup ? ReadMarkup(line, source, level) : null;
            ProgramDomain domain = LoadDomain();
            Style style = WorldStyle(domain, level, line.Style);
            (Profile profile, ScanType scanType, string description) = Drawing(domain, style, dimension, line.Cut);

            ChunkMapperPack pack = new(domain.DefaultAssetPack);
            pack.UpdatePack(profile, scanType);

            MarkupImage? marks = markup is null ? null : new MarkupImage(markup, dimension.Namespace);
            if (marks is { HasAny: false })
            {
                Console.Error.WriteLine($"No pins or zones are marked in {The(dimension)}; drawing the map without them");
                marks = null;
            }

            ConsoleProgress progress = new();
            MapImageBounds? bounds = MapImageExporter.Export(pack, new SceneInfo(level, dimension), line.Out!, line.Area, progress, cancellation, marks is null ? null : marks.Draw);
            progress.Finish();

            if (bounds is not MapImageBounds drawn)
            {
                Console.Error.WriteLine(line.Area is null ? $"{level.WorldName} has nothing in {The(dimension)}" : $"{level.WorldName} has nothing in that area of {The(dimension)}");
                return 1;
            }

            if (marks is not null && markup is not null) description += $", {Count(markup.PinsIn(dimension.Namespace).Count(), "pin")} and {Count(markup.ZonesIn(dimension.Namespace).Count(), "zone")}";
            Console.WriteLine($"Wrote {line.Out}: {drawn.Width:N0} x {drawn.Height:N0} px, {level.WorldName}'s {dimension.Name} ({description}), " +
                $"blocks {drawn.X}, {drawn.Z} to {drawn.X + drawn.Width - 1}, {drawn.Z + drawn.Height - 1}");
            return 0;
        }

        // "the Overworld", "The End"
        private static string The(Dimension dimension) => dimension.Name.StartsWith("The ") ? dimension.Name : $"the {dimension.Name}";

        private static string Count(int count, string what) => $"{count} {what}{(count == 1 ? "" : "s")}";

        // The file given, or what's marked on the world in Mapee (kept in Mapee's settings folder, found by the
        // world's path, so a world named here the way Mapee opened it finds the same markup)
        private static MarkupDocument ReadMarkup(CommandLine line, string source, Level level)
        {
            if (line.MarkupFile is not string file) return MarkupStore.Default.Load(source, level.WorldName);
            try
            {
                return MarkupStore.Read(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                throw new UsageException($"Couldn't read markup from {file}: {e.Message}");
            }
        }

        private static int WebMap(CommandLine line, CancellationToken cancellation)
        {
            IList<Dimension> dimensions = line.Dimensions ?? [.. WebMapFolder.Dimensions.Select(d => d.Dimension)];
            string folder = line.Out!;

            // An earlier web map is replaced without asking, so a map can be refreshed from a script, and only what
            // changed in the world since is drawn again
            IList<string> replaced = WebMapFolder.WillReplace(folder, dimensions.Select(WebMapFolder.IdOf));
            if (replaced.Count > 0 && !WebMapFolder.IsEarlierExport(folder) && !line.Force)
            {
                throw new UsageException($"{folder} already has {string.Join(", ", replaced)}, which the web map would replace; add --force to replace them");
            }

            (Level level, string source) = OpenWorld(line.World!);
            MarkupDocument? markup = line.Markers ? ReadMarkup(line, source, level) : null;
            ProgramDomain domain = LoadDomain();
            Style style = WorldStyle(domain, level, line.Style);

            List<WebMapTarget> targets = [.. dimensions.Select(dimension =>
            {
                (Profile profile, ScanType scanType, string description) = Drawing(domain, style, dimension, line.Cut);
                return new WebMapTarget(dimension, profile, scanType, description, line.Markers ? WebMapSaver.Markers(level, dimension) : [],
                    WebMapSaver.DrawingKey(style, profile, scanType, level));
            })];

            // The page opens on the player's dimension
            string current = WebMapFolder.IdOf(dimensions.Contains(level.Player.Dimension) ? level.Player.Dimension : dimensions[0]);

            ConsoleProgress progress = new();
            IList<WebMapDimension> written = WebMapSaver.Export(domain.DefaultAssetPack, level, targets, folder, level.WorldName ?? "Map", current, progress, cancellation, markup, line.Full);
            progress.Finish();

            if (written.Count == 0)
            {
                Console.Error.WriteLine($"{level.WorldName} has nothing in {string.Join(", ", dimensions.Select(d => d.Name))}");
                return 1;
            }

            Console.WriteLine($"Wrote {Path.Combine(folder, "index.html")}: {level.WorldName}, " +
                string.Join(", ", written.Select(d => $"{d.Name} {d.Regions.Count} region{(d.Regions.Count == 1 ? "" : "s")}")) + $" ({targets[0].Subtitle})" +
                (markup is { IsEmpty: false } ? $", {Count(markup.Pins.Count, "pin")} and {Count(markup.Zones.Count, "zone")}" : ""));
            if (WebMapSaver.DescribeUpdate(written) is { Length: > 0 } update) Console.WriteLine($"Updated: {update}");
            return 0;
        }

        // A search stops at BlockFinder.MaxMatches, so the output can't run away with a common block
        private static int Find(CommandLine line, CancellationToken cancellation)
        {
            Dimension dimension = line.Dimensions?.Count switch
            {
                null => Dimension.Overworld,
                1 => line.Dimensions[0],
                _ => throw new UsageException("find searches one dimension; give one to --dimension")
            };

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            List<FindTarget> targets = [];
            foreach (string name in line.Blocks!)
            {
                if (FindGroups.Find(name) is FindTarget group) targets.Add(group);
                else if (KnownBlocks.Contains(name)) targets.Add(FindTarget.Of(name));
                else throw new UsageException($"Mapee doesn't know a block called {name}; give its Java name, such as diamond_ore, or a group: {string.Join(", ", FindGroups.Named.Select(n => n.Id))}");
            }

            (Level level, _) = OpenWorld(line.World!);
            SceneInfo scene = new(level, dimension);
            if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);

            List<Coords> regions = [.. scene.Regions];
            if (line.Area is BlockArea area)
            {
                regions = [.. regions.Where(r => r.X * 512 <= area.MaxX && r.X * 512 + 511 >= area.MinX && r.Z * 512 <= area.MaxZ && r.Z * 512 + 511 >= area.MinZ)];
            }
            if (regions.Count == 0)
            {
                Console.Error.WriteLine(line.Area is null ? $"{level.WorldName} has nothing in {The(dimension)}" : $"{level.WorldName} has nothing in that area of {The(dimension)}");
                return 1;
            }

            BlockQuery query = new(targets, line.MinY, line.MaxY);
            FindResults results = new(query);
            bool interactive = !Console.IsErrorRedirected;
            int searched = 0;
            bool complete = new BlockFinder(query).Search(scene.RegionStore, regions, (region, matches) =>
            {
                results.Set(region, matches);
                int done = Interlocked.Increment(ref searched);
                if (interactive) Console.Error.Write($"\rSearching {The(dimension)}: {done}/{regions.Count} regions");
            }, cancellation);
            if (interactive) Console.Error.WriteLine();

            List<BlockMatch> found = line.Area is BlockArea within ? results.MatchesIn(within.MinX, within.MinZ, within.MaxX, within.MaxZ) : results.All();
            found.Sort((a, b) => a.Target != b.Target ? a.Target.CompareTo(b.Target) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y.CompareTo(b.Y));

            if (line.Csv) Console.WriteLine("x,y,z,block");
            foreach (BlockMatch match in found)
            {
                string what = targets[match.Target].Label;
                Console.WriteLine(line.Csv ? $"{match.X},{match.Y},{match.Z},\"{what.Replace("\"", "\"\"")}\"" : $"{match.X} {match.Y} {match.Z}  {what}");
            }

            int[] counts = new int[targets.Count];
            foreach (BlockMatch match in found) counts[match.Target]++;
            Console.Error.WriteLine(found.Count == 0
                ? $"Found none in {level.WorldName}'s {dimension.Name}"
                : $"Found {found.Count:N0} in {level.WorldName}'s {dimension.Name}: {string.Join(", ", targets.Select((t, i) => $"{t.Label} {counts[i]:N0}"))}");
            if (!complete) Console.Error.WriteLine($"Stopped at {BlockFinder.MaxMatches:N0}; search for something rarer, or narrow the heights or area");
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

        // The world, and what it was opened from: a folder or a .mcworld, as Mapee remembers it
        private static (Level Level, string Source) OpenWorld(string world)
        {
            string path = WorldLocation.IsWorld(world) ? world : FindNamedWorld(world);
            Level level = WorldLocation.Read(path) ?? throw new InvalidDataException($"Couldn't read the world at {path}");
            Scene.DetectAlphaWorld(level);
            return (level, path);
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

            string stage = $"{value.Dimension}: {value.Stage switch
            {
                WebMapStage.Rendering when value.Update => "drawing changed regions",
                WebMapStage.Rendering => "drawing regions",
                _ => "making zoom levels"
            }}";
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
        // Drawing pins and zones: render only does when asked; the web map does unless markers are left out
        public bool Markup { get; private set; }
        public string? MarkupFile { get; private set; }
        public bool Force { get; private set; }
        public bool Full { get; private set; }
        public IList<string>? Blocks { get; private set; }
        public int? MinY { get; private set; }
        public int? MaxY { get; private set; }
        public bool Csv { get; private set; }

        private static readonly string[] Commands = ["render", "webmap", "find", "worlds", "styles"];

        // Paths are made full here, before the program moves to its own folder to read its resources
        public static CommandLine Parse(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            if (!Commands.Contains(command)) throw new UsageException($"{args[0]} isn't a command; the commands are {string.Join(", ", Commands)}");

            CommandLine line = new() { Command = command };
            bool drawing = command is "render" or "webmap" or "find";

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
                else if (option is "--out" or "--dimension" or "--style" or "--slice" or "--caves" or "--area" or "--block" or "--y")
                {
                    if (i + 1 >= args.Length) throw new UsageException($"{option} needs a value");
                    value = args[++i];
                }
                // A file after --markup is optional
                else if (option == "--markup" && i + 1 < args.Length && !args[i + 1].StartsWith("--") && args[i + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
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
                    case "--style" when command != "find":
                        line.Style = value;
                        break;
                    case "--slice" when command != "find":
                    case "--caves" when command != "find":
                        if (line.Cut is not null) throw new UsageException("Give --slice or --caves, not both");
                        if (!short.TryParse(value, out short y)) throw new UsageException($"{option} needs a height, such as 64, not {value}");
                        line.Cut = new HeightCut(y, option == "--caves");
                        break;
                    case "--area" when command is "render" or "find":
                        if (!BlockArea.TryParse(value, out BlockArea area)) throw new UsageException($"--area needs two corners as x1,z1,x2,z2, such as -500,-500,500,500, not {value}");
                        line.Area = area;
                        break;
                    case "--no-markers" when command == "webmap":
                        line.Markers = false;
                        break;
                    case "--markup" when command != "find":
                        line.Markup = true;
                        if (value.Length > 0)
                        {
                            if (!File.Exists(value)) throw new UsageException($"There's no markup file at {value}");
                            line.MarkupFile = Path.GetFullPath(value);
                        }
                        break;
                    case "--force" when command == "webmap":
                        line.Force = true;
                        break;
                    case "--full" when command == "webmap":
                        line.Full = true;
                        break;
                    case "--block" when command == "find":
                        line.Blocks = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                        if (line.Blocks.Count == 0) throw new UsageException("--block needs a block, such as diamond_ore");
                        break;
                    case "--y" when command == "find":
                        string[] heights = value.Split(',', StringSplitOptions.TrimEntries);
                        if (heights.Length != 2 || !int.TryParse(heights[0], out int minY) || !int.TryParse(heights[1], out int maxY))
                        {
                            throw new UsageException($"--y needs two heights as min,max, such as -64,16, not {value}");
                        }
                        (line.MinY, line.MaxY) = (Math.Min(minY, maxY), Math.Max(minY, maxY));
                        break;
                    case "--csv" when command == "find":
                        line.Csv = true;
                        break;
                    default:
                        throw new UsageException($"{command} has no option {option}");
                }
            }

            if (command == "webmap" && line.Markers) line.Markup = true;
            if (command == "webmap" && !line.Markers && line.MarkupFile is not null) throw new UsageException("--no-markers leaves the pins and zones out; give --markup or --no-markers, not both");

            if (drawing)
            {
                if (line.World is null) throw new UsageException($"{command} needs a world");
                if (command == "find")
                {
                    if (line.Blocks is null) throw new UsageException("find needs --block, the blocks to look for");
                    if (line.Out is not null) throw new UsageException("find has no option --out; it writes to the screen (send it to a file with >)");
                    return line;
                }
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
