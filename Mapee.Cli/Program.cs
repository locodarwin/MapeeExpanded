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
                  --markup                 draw the pins, zones and signs marked on the world in Mapee
              mapee-cli webmap <world> --out <folder> [options]
                  Exports a zoomable web map (open index.html), every dimension the world has.
                  --dimension <d>[,<d>]    only these dimensions
                  --no-markers             leave out the spawn and player markers, and the pins, zones and signs
                  --force                  replace files in a folder that isn't an earlier web map
                  --full                   draw every region again; otherwise, over an earlier web map
                                           of the same world drawn the same way, only regions the game
                                           has saved since are drawn
              mapee-cli watch <world> --out <folder> [--every <seconds>] [options]
                  Exports a web map as webmap does, then keeps it up to date while the game is
                  played: every 30 seconds (or --every) it looks for saves and draws the regions
                  saved since. Takes webmap's options. Ctrl+C stops it.
              mapee-cli find <world> --block <name>[,<name>] [options]
                  Lists where blocks are, one line per block found (x y z, then what it is).
                  Names are as Java has them (diamond_ore, ancient_debris), for Bedrock worlds too,
                  or one of the groups: diamonds, ancient_debris, spawners, chests, emeralds, buried.
                  --dimension <d>          overworld (default), nether, end
                  --area <x1>,<z1>,<x2>,<z2>  only between these corners
                  --y <min>,<max>          only between these heights
                  --csv                    as comma-separated values with a header line
              mapee-cli portals <world> [--csv]
                  Lists the Nether portals in the Overworld and the Nether, where each leads, and
                  which lead to a portal that leads somewhere else on the way back.
              mapee-cli structures <world> [options]
                  Lists the villages, strongholds, temples and other structures the game has
                  generated, every dimension the world has. Java worlds save them all; Bedrock
                  saves only fortresses, witch huts, monuments, outposts and villages, and
                  strongholds, ancient cities and trial chambers are found by their blocks.
                  --dimension <d>[,<d>]    only these dimensions
                  --csv                    as comma-separated values with a header line
              mapee-cli signs <world> [options]
                  Lists the signs and what they say, every dimension the world has, blank ones left out.
                  --dimension <d>[,<d>]    only these dimensions
                  --text <words>           only signs saying all these words, in any case
                  --csv                    as comma-separated values with a header line
              mapee-cli worlds             lists the worlds in the game's world folders
              mapee-cli styles             lists the styles

            Options for render, webmap and watch:
              --style <id or name>         how the map is coloured (default: Default; see mapee-cli styles)
              --slice <y>                  draw the world cut at height y, as the height panel's Slice
              --caves <y>                  draw the caves below height y, as the height panel's Caves
              --markup <file.json>         the pins, zones and signs in a file exported from Mapee, in place of
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
                    "portals" => Portals(line, cancel.Token),
                    "structures" => Structures(line, cancel.Token),
                    "signs" => Signs(line, cancel.Token),
                    "watch" => Watch(line, cancel.Token),
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

            if (marks is not null && markup is not null)
            {
                int signs = markup.SignsIn(dimension.Namespace).Count();
                description += $", {Count(markup.PinsIn(dimension.Namespace).Count(), "pin")}{(signs > 0 ? $", {Count(signs, "sign")}" : "")} and {Count(markup.ZonesIn(dimension.Namespace).Count(), "zone")}";
            }
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

        private static int WebMap(CommandLine line, CancellationToken cancellation) => WebMap(line, null, cancellation);

        // domain is loaded here if not given; it can be loaded only once. Given a time, as watch's updates are, it
        // says in one line after it what was drawn again
        private static int WebMap(CommandLine line, ProgramDomain? domain, CancellationToken cancellation, DateTime? updatedAt = null)
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
            domain ??= LoadDomain();
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
            if (updatedAt is DateTime time)
            {
                Console.WriteLine($"{time:HH:mm:ss}  {(WebMapSaver.DescribeUpdate(written) is { Length: > 0 } changed ? changed : "every region drawn again")}");
                return 0;
            }

            Console.WriteLine($"Wrote {Path.Combine(folder, "index.html")}: {level.WorldName}, " +
                string.Join(", ", written.Select(d => $"{d.Name} {d.Regions.Count} region{(d.Regions.Count == 1 ? "" : "s")}")) + $" ({targets[0].Subtitle})" +
                (markup is { IsEmpty: false } ? $", {Count(markup.Pins.Count, "pin")}{(markup.Signs.Count > 0 ? $", {Count(markup.Signs.Count, "sign")}" : "")} and {Count(markup.Zones.Count, "zone")}" : ""));
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

        // One line per portal: where it is, where it leads and whether it leads back
        private static int Portals(CommandLine line, CancellationToken cancellation)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            (Level level, _) = OpenWorld(line.World!);

            SceneInfo? Scene(Dimension dimension)
            {
                SceneInfo scene = new(level, dimension);
                if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);
                return scene.Regions.Any() ? scene : null;
            }
            bool interactive = !Console.IsErrorRedirected;
            if (interactive) Console.Error.Write("Looking for portals in the Overworld");
            SceneInfo? overworld = Scene(Dimension.Overworld);
            List<BlockMatch> overworldBlocks = PortalMap.Search(overworld, cancellation);
            if (interactive) Console.Error.Write(" and the Nether");
            SceneInfo? nether = Scene(Dimension.Nether);
            List<BlockMatch> netherBlocks = PortalMap.Search(nether, cancellation);
            if (interactive) Console.Error.WriteLine();

            PortalMap map = new(overworldBlocks, netherBlocks);
            if (line.Csv) Console.WriteLine("dimension,x,y,z,blocks,leads_to_dimension,target_x,target_y,target_z,leads_to_x,leads_to_y,leads_to_z,leads_back");
            foreach (Portal portal in map.Overworld.Concat(map.Nether))
            {
                PortalLink link = map.Link(portal);
                Dimension other = portal.Dimension == Dimension.Nether ? Dimension.Overworld : Dimension.Nether;
                if (line.Csv)
                {
                    string to = link.To is Portal p ? $"{p.X},{p.MinY},{p.Z}" : ",,";
                    string back = link.To is null ? "" : link.Mismatched ? "no" : "yes";
                    Console.WriteLine($"{portal.Dimension.Name},{portal.X},{portal.MinY},{portal.Z},{portal.Blocks},{other.Name},{link.TargetX},{link.TargetY},{link.TargetZ},{to},{back}");
                    continue;
                }

                string where = link.To is Portal found
                    ? $"leads to the portal at {found} in {The(other)}"
                    : $"would build a new portal near {link.TargetX}, {link.TargetY}, {link.TargetZ} in {The(other)}";
                string warning = link.Mismatched ? $"; that one leads back to {link.Back} instead" : "";
                Console.WriteLine($"{portal.Dimension.Name} {portal}: {where}{warning}");
            }

            int mismatched = map.Overworld.Concat(map.Nether).Count(p => map.Link(p).Mismatched);
            Console.Error.WriteLine(map.Overworld.Count + map.Nether.Count == 0
                ? $"{level.WorldName} has no Nether portals"
                : $"{level.WorldName}: {map.Overworld.Count} in the Overworld, {map.Nether.Count} in the Nether{(mismatched switch { 0 => "", 1 => "; 1 leads to a portal that doesn't lead back", _ => $"; {mismatched} lead to portals that don't lead back" })}");
            return 0;
        }

        // One line per structure: what it is and where its middle is
        private static int Structures(CommandLine line, CancellationToken cancellation)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            (Level level, _) = OpenWorld(line.World!);
            IList<Dimension> dimensions = line.Dimensions ?? [Dimension.Overworld, Dimension.Nether, Dimension.TheEnd];

            bool interactive = !Console.IsErrorRedirected;
            List<(Dimension Dimension, List<Structure> Found)> all = [];
            foreach (Dimension dimension in dimensions)
            {
                SceneInfo scene = new(level, dimension);
                if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);
                if (!scene.Regions.Any()) continue;

                if (interactive) Console.Error.Write($"\rLooking for structures in {The(dimension)}".PadRight(48));
                all.Add((dimension, StructureMap.Read(scene, cancellation)));
            }
            if (interactive) Console.Error.Write("\r".PadRight(49) + "\r");

            if (line.Csv) Console.WriteLine("dimension,structure,x,y,z,min_x,min_y,min_z,max_x,max_y,max_z,known_by");
            foreach (var (dimension, found) in all)
            {
                foreach (Structure s in found)
                {
                    string by = s.Source == StructureSource.Blocks ? "blocks" : "saved";
                    Console.WriteLine(line.Csv
                        ? $"{dimension.Name},\"{s.Name.Replace("\"", "\"\"")}\",{s.X},{s.Y},{s.Z},{s.MinX},{s.MinY},{s.MinZ},{s.MaxX},{s.MaxY},{s.MaxZ},{by}"
                        : $"{dimension.Name} {s.Name} at {s}{(s.Source == StructureSource.Blocks ? " (found by its blocks)" : "")}");
                }
            }

            int total = all.Sum(d => d.Found.Count);
            if (all.Count == 0)
            {
                Console.Error.WriteLine($"{level.WorldName} has nothing in {string.Join(", ", dimensions.Select(The))}");
                return 1;
            }
            Console.Error.WriteLine(total == 0
                ? $"{level.WorldName} has no structures saved{(level.Edition == Edition.Bedrock ? " or found" : "")}"
                : $"{level.WorldName}: " + string.Join(", ", all.Where(d => d.Found.Count > 0).Select(d => $"{d.Found.Count} in {The(d.Dimension)}")));
            return 0;
        }

        private static int Signs(CommandLine line, CancellationToken cancellation)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            (Level level, _) = OpenWorld(line.World!);
            IList<Dimension> dimensions = line.Dimensions ?? [Dimension.Overworld, Dimension.Nether, Dimension.TheEnd];
            string[] words = line.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

            bool interactive = !Console.IsErrorRedirected;
            List<(Dimension Dimension, List<Sign> Found)> all = [];
            foreach (Dimension dimension in dimensions)
            {
                SceneInfo scene = new(level, dimension);
                if (scene.RegionStore is IBackgroundRegionStore background) background.Ready.Wait(cancellation);
                if (!scene.Regions.Any()) continue;

                if (interactive) Console.Error.Write($"\rReading the signs in {The(dimension)}".PadRight(48));
                all.Add((dimension, [.. SignMap.Read(scene, cancellation).Where(s => !s.IsBlank && SignText.Says(s, words))]));
            }
            if (interactive) Console.Error.Write("\r".PadRight(49) + "\r");

            static string Csv(string text) => $"\"{text.Replace("\"", "\"\"")}\"";
            if (line.Csv) Console.WriteLine("dimension,x,y,z,kind,front,back");
            foreach (var (dimension, found) in all)
            {
                foreach (Sign s in found)
                {
                    Console.WriteLine(line.Csv
                        ? $"{dimension.Name},{s.X},{s.Y},{s.Z},{(s.Hanging ? "hanging" : "sign")},{Csv(string.Join("\n", s.Front))},{Csv(string.Join("\n", s.Back))}"
                        : $"{dimension.Name} {s} {(s.Hanging ? "hanging sign" : "sign")}: {s.Text}");
                }
            }

            int total = all.Sum(d => d.Found.Count);
            if (all.Count == 0)
            {
                Console.Error.WriteLine($"{level.WorldName} has nothing in {string.Join(", ", dimensions.Select(The))}");
                return 1;
            }
            string what = words.Length > 0 ? $"signs saying {line.Text}" : "signs with anything on them";
            Console.Error.WriteLine(total == 0
                ? $"{level.WorldName} has no {what}"
                : $"{level.WorldName}: " + string.Join(", ", all.Where(d => d.Found.Count > 0).Select(d => $"{d.Found.Count} in {The(d.Dimension)}")));
            return 0;
        }

        // The web map once, then again whenever the world's files change, until Ctrl+C
        private static int Watch(CommandLine line, CancellationToken cancellation)
        {
            (_, string source) = OpenWorld(line.World!);
            if (File.Exists(source) && !string.Equals(Path.GetFileName(source), "level.dat", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException($"{Path.GetFileName(source)} is a world packed in one file, which the game doesn't play from; give the world's folder");
            }
            string folder = Directory.Exists(source) ? source : Path.GetDirectoryName(source)!;

            ProgramDomain domain = LoadDomain();
            string? stamp = WorldStamp(folder);
            int result = WebMap(line, domain, cancellation);
            if (result != 0) return result;

            Console.WriteLine($"Watching {folder} every {line.Every} s; Ctrl+C stops");
            while (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(line.Every)))
            {
                string? now = WorldStamp(folder);
                if (now is not null && now == stamp) continue;

                try
                {
                    WebMap(line, domain, cancellation, DateTime.Now);
                    stamp = now;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e) when (e is not UsageException)
                {
                    // The game may be partway through a save; the next look tries again
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"mapee-cli: couldn't update the web map: {e.Message}; trying again in {line.Every} s");
                }
            }
            Console.WriteLine("Stopped watching");
            return 0;
        }

        // Every file in the world folder, its size and time, folded into one string; null if the folder can't be
        // read just now. The game's session.lock changes without a save
        private static string? WorldStamp(string folder)
        {
            try
            {
                System.Text.StringBuilder output = new();
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.FullName, StringComparer.Ordinal))
                {
                    if (file.Name == "session.lock") continue;
                    output.Append(file.FullName).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
                }
                return output.ToString();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
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
        // The words signs must say
        public string? Text { get; private set; }
        // How often watch looks for saves, in seconds
        public int Every { get; private set; } = 30;

        private static readonly string[] Commands = ["render", "webmap", "watch", "find", "portals", "structures", "signs", "worlds", "styles"];

        // Paths are made full here, before the program moves to its own folder to read its resources
        public static CommandLine Parse(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            if (!Commands.Contains(command)) throw new UsageException($"{args[0]} isn't a command; the commands are {string.Join(", ", Commands)}");

            CommandLine line = new() { Command = command };
            bool drawing = command is "render" or "webmap" or "watch" or "find" or "portals" or "structures" or "signs";
            bool webMap = command is "webmap" or "watch";

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
                else if (option is "--out" or "--dimension" or "--style" or "--slice" or "--caves" or "--area" or "--block" or "--y" or "--every" or "--text")
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
                    case "--no-markers" when webMap:
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
                    case "--force" when webMap:
                        line.Force = true;
                        break;
                    case "--full" when webMap:
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
                    case "--csv" when command is "find" or "portals" or "structures" or "signs":
                        line.Csv = true;
                        break;
                    case "--text" when command == "signs":
                        line.Text = value;
                        break;
                    case "--every" when command == "watch":
                        if (!int.TryParse(value, out int every) || every < 2) throw new UsageException($"--every needs a number of seconds, 2 or more, not {value}");
                        line.Every = every;
                        break;
                    default:
                        throw new UsageException($"{command} has no option {option}");
                }
            }

            if (webMap && line.Markers) line.Markup = true;
            if (webMap && !line.Markers && line.MarkupFile is not null) throw new UsageException("--no-markers leaves the pins and zones out; give --markup or --no-markers, not both");

            if (drawing)
            {
                if (line.World is null) throw new UsageException($"{command} needs a world");
                if (command == "portals")
                {
                    if (line.Out is not null || line.Dimensions is not null || line.Style is not null || line.Cut is not null || line.Area is not null || line.Markup)
                    {
                        throw new UsageException("portals takes only --csv; it looks in the Overworld and the Nether, and writes to the screen");
                    }
                    return line;
                }
                if (command is "structures" or "signs")
                {
                    if (line.Out is not null || line.Style is not null || line.Cut is not null || line.Area is not null || line.Markup)
                    {
                        throw new UsageException($"{command} takes only --dimension{(command == "signs" ? ", --text" : "")} and --csv, and writes to the screen");
                    }
                    return line;
                }
                if (command == "find")
                {
                    if (line.Blocks is null) throw new UsageException("find needs --block, the blocks to look for");
                    if (line.Out is not null) throw new UsageException("find has no option --out; it writes to the screen (send it to a file with >)");
                    return line;
                }
                if (line.Out is null) throw new UsageException($"{command} needs --out, {(command == "render" ? "the PNG to write" : "the folder to write the web map into")}");
                if (command == "watch" && line.Full) throw new UsageException("watch draws only what the game saves; to draw everything again, run webmap --full first");
                if (command == "render" && Directory.Exists(line.Out)) throw new UsageException($"{line.Out} is a folder; --out names the PNG to write");
                if (webMap && File.Exists(line.Out)) throw new UsageException($"{line.Out} is a file; --out names the folder to write the web map into");
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
