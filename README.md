# Mapee Expanded

A fork of [Mapee](https://github.com/Voxelview/Mapee), the simple, yet powerful Minecraft mapper & filter, that adds **Bedrock Edition** worlds.<br>
Loads Java worlds from Alpha to the newest release, and Bedrock worlds from Pocket Edition 0.9 to the newest release.<br>
Upstream website: [mapee.net](https://www.mapee.net/home)

[Download the latest release](https://github.com/locodarwin/MapeeExpanded/releases/latest): for Windows x64, unzip and run `Mapee.exe`; for Linux x86-64, the AppImage (see "Linux" below). Neither needs installing.

# What's New?
**Since 1.5.0** (not in a release yet)
- Change any shortcut key: click it in the key list and press the new one (see "Keyboard shortcuts" below)
- Java worlds from 26.1 on show their Nether (Mapee looked in the wrong folder), and the player is placed where they really are, in the right dimension, rather than at the world spawn
- Web maps update: exporting a web map again into the same folder draws only the regions where the game has saved something since, and keeps the rest, so refreshing a big map takes seconds. Changing the style, block filter, height cut or render settings, or a new version of Mapee, draws everything again, as does the export window's **Redraw everything** (`--full` for `mapee-cli`). A web map made in the app can be updated from the command line and the other way round
- `mapee-cli render` images now match the web map's tiles pixel for pixel (both draw regions in the same groups of four, so neighbours are shaded the same way)

**Version 1.5.0**
- Linux: an AppImage of Mapee, a second GUI written in Avalonia that looks and works like the Windows one (see "Linux" below)
- Remembers between runs: the window size, the style, which overlays are on (grid, slime chunks, axes, chunk highlight), and the worlds you opened recently, which head the world browser even when they live outside the usual folders. Reopening a world returns to the dimension, place and zoom you left it at. Settings are kept in `%APPDATA%\Mapee\Settings.json` (Windows) or `~/.config/Mapee/Settings.json` (Linux)
- Opens a world named on the command line (its folder, `level.dat` or a `.mcworld`), so a world can be dropped on `Mapee.exe` or opened with Mapee from a file manager
- Bedrock sky light spreads sideways into cave entrances and under overhangs, as in Java
- Older Bedrock worlds: chunks last saved before 1.18, all the way back to Pocket Edition 0.9, load with their blocks, biomes and heights. Blocks saved under old names (wool before it was split by colour, logs, stone variants and the like) are translated too, which also fixes stray chunks in newer worlds that hadn't been visited since an update
- Height panel on the right of the map: **Surface** is the style's own view, **Slice** draws the map as if the world stopped at the chosen height, and **Caves** shows the floors of the caves below it. Drag the slider or scroll over it (Shift for steps of 10); each dimension keeps its own height
- The info bar names the block under the mouse with its height and biome, as the map shows it (so it follows the height panel), and for water or lava, how deep it is and what's at the bottom
- Export as a web map: the export window's new **Web map** option renders the whole world, not only what has loaded, into a folder holding `index.html` and a tile pyramid for each of the Overworld, Nether and End the world has, each in the current style (and height cut, where the height panel is on). The page pans and zooms like an online map (mouse, touch, keyboard), switches between the dimensions, shows the block's X, Y and Z beside a resting mouse as Mapee does, marks the spawn and the player, has a region and chunk grid, and keeps the dimension and view in the address so a link opens where it was copied. It needs no internet and no server: open it from disk or put the folder on any web host. Any folder will do; if it already holds web map files, Mapee names them and asks before replacing them
- Coordinates beside the mouse: rest the mouse over the map for a moment and a small label gives the block's X, Y and Z
- Command line: `mapee-cli` draws a world without opening a window, a dimension into one PNG or the whole world into a web map, with the same styles and height cuts as the app (see "Command line" below)
- Markup: mark the map with labelled **pins** and labelled **zones** (territories, claims, farms, towns), each with a colour, a note, and for pins one of eight symbols. They're kept per world and per dimension, show in the Go to window and the web map, and can be drawn into exported images (see "Markup" below)
- Keyboard shortcuts for the dimensions, zoom, styles, height panel, overlays, markup and windows; F1, ? or the toolbar's keyboard button lists them, and each toolbar button's tooltip names its key (see "Keyboard shortcuts" below)
- **Recent** button beside Load world: the worlds opened lately, newest first, with their edition and when they were last opened; picking one opens it where the map was left. An × takes a world off the list (the world itself is never touched), **Clear the list** forgets all but the open one, and a world that has been moved or deleted shows dimmed and offers to come off the list. Ctrl+Shift+O opens it from the keyboard (Up, Down, Enter)
- Live reload: while a world is open, the map redraws what the game saves, chunk by chunk, and the info bar says what was updated and when. The toolbar's **Live** button (or W) turns it off and on. Java saves every five minutes, when the game is paused, or on `/save-all`, so that's how far behind Java can be; Bedrock writes more often. Worlds opened from a `.mcworld` are copies and don't change (see "Live reload" below)
- Drag and drop: drop a world folder, its `level.dat` or a `.mcworld` on the window to open it, or a markup `.json` file to add its pins and zones to the world on show. While dragging, the window says what dropping would do

**Version 1.4.0**
- Opens Bedrock Edition worlds: the world browser lists them next to Java worlds, and the open button takes a `level.dat` or a `.mcworld` file
- Bedrock block states and biomes are translated to Java's, so every style, filter and render setting works as it does for Java worlds
- Light is computed for Bedrock worlds, which don't save it, so night mode and cave views work
- Large worlds are indexed in the background; the map shows "Reading world" until it can draw
- No update check: the upstream updater would replace this fork with upstream Mapee

**Version 1.2.0** (upstream)
- Added Alpha support
- Added new blocks in 26.2

# Linux

The Linux build is a single AppImage, self-contained (no .NET needed). Make it executable and run it:

    chmod +x MapeeExpanded-1.5.0-x86_64.AppImage
    ./MapeeExpanded-1.5.0-x86_64.AppImage [world]

If it complains about FUSE, run it with `--appimage-extract-and-run`. It runs as a native Wayland app (following the monitor's scale, fractional ones too) and falls back to X11, where it takes the scale from `GDK_SCALE` or `AVALONIA_GLOBAL_SCALE_FACTOR`. The world browser looks in `~/.minecraft/saves` (Java) and `~/.local/share/mcpelauncher/games/com.mojang/minecraftWorlds` (Bedrock through mcpelauncher); anything else opens with Browse, or by dropping it on the window. Differences from Windows: the system's own window frame, and Go to, Add block and the custom-dimension list open as pop-ups under their buttons rather than as small windows. Given a command as its first argument (`render`, `webmap`, `worlds`, `styles`), the AppImage runs `mapee-cli` instead (see "Command line" below).

# Markup

The toolbar's **Markup** button (the flag) opens a bar under the toolbar with what a click on the map does:

- **Pin**: click to drop a pin on the block, and give it a label, colour, symbol and note. Click a pin to change it.
- **Zone**: click each corner; double-click, press Enter or click the first corner to finish. Backspace takes the last corner back; hold Shift to snap corners to chunk corners.
- **Rectangle**: drag out a rectangular zone; hold Shift to take in whole chunks.
- **Select**: click a pin or zone to pick it, double-click to change it, Delete to remove it; drag a pin, or a picked zone's corners, to move them. On a picked zone, double-click an edge to add a corner there, or a corner to remove it.

Dragging still moves the map in every mode, and a right-click on any pin or zone changes it even with the bar closed. Hovering over a zone shows its size in blocks and chunks. Ctrl+Z and Ctrl+Y undo and redo while the bar is open. **Places** lists the dimension's pins and zones (click one to go there), and the Go to window lists them too. **Export** and **Import** move markup between machines or onto a copy of a world; **Hide** hides it until the Markup button (or Shift+M) brings it back. P, Z, R and V pick the four modes from the keyboard, opening the bar if it's closed.

Markup is saved as you go, one file per world in the settings folder (`%APPDATA%\Mapee\Markup` on Windows, `~/.config/Mapee/Markup` on Linux), never in the world itself. It follows the path the world was opened from, as the recent worlds do, so a copied world starts without it until you import it. The export window's **Include pins and zones** puts it in screenshots, full-resolution images and web maps; in a web map, click a pin or zone to see its note, and **PINS** and **ZONES** (M and Z) switch them off.

# Live reload

With **Live** on (the circling arrows on the toolbar, on by default and remembered), Mapee looks at the open world's files every two seconds. That look is only a folder listing; nothing is read until the files have stopped changing for a whole look, so a save in progress is never read halfway (if the game keeps writing for ten seconds, Mapee reads anyway). Then:

- **Java**: each region file that changed is drawn again; the save times inside it say how many chunks the game wrote. Newly explored regions appear.
- **Bedrock**: Mapee opens the world's database again and reads only the records written since its last look (every record carries a sequence number), so it knows exactly which chunks changed without reading the rest of the world. Light reaches across chunk edges, so a chunk on a region's edge redraws the region beside it too.

Each region keeps its old picture until the new one is ready, so the map doesn't flash. Only the dimension on screen is watched; the others catch up when you switch to them. The player's position (Go to's **Player position**) follows `level.dat`. If the game keeps its files locked, the info bar says Mapee can't read the world and it tries again.


# Keyboard shortcuts
Press **F1** or **?** in the app, or the keyboard button at the left end of the toolbar, for this list. None of them work while you're typing in a box, and the ones that need a world do nothing until one is open.

| Keys | Does |
|---|---|
| Arrows | Move the map; with Shift, a quarter of the screen at a time |
| + − | Zoom in, out |
| 0 or Ctrl+R | Back to 100% |
| 1 2 3 | Overworld, Nether, End |
| 4 – 9 | Custom dimensions, in their list's order |
| , . | Previous, next style |
| H | Height panel: Surface, Slice, Caves in turn |
| [ ] | Height down, up; with Shift, by 10 |
| G S A C | Grid lines, slime chunks, axes, chunk highlighter |
| L | Measure length |
| N | Night mode |
| W | Live reload on or off |
| M | Markup bar |
| Shift+M | Show or hide pins and zones |
| P Z R V | Markup: Pin, Zone, Rectangle, Select |
| Ctrl+Z Ctrl+Y | Undo, redo markup (while the bar is open) |
| Ctrl+G | Go to a place, with the cursor in the coordinate box |
| Ctrl+E | Export as image or web map |
| Ctrl+O | Load a world |
| Ctrl+Shift+O | Recent worlds (Up, Down and Enter pick one) |
| Ctrl+F | Filter blocks |
| F11 | Full screen, on Windows (Esc leaves it); on Linux the window manager's own key does this |

To change a key, click it in that list and press the new one, with Ctrl or Shift if you like (Esc cancels). If another action had that key, it moves, and the list says which action lost it; that action has no key until you give it one. **Reset** beside a changed key gives back the one it came with, and **Reset all keys** undoes every change. The arrows, the 1 – 9 dimension keys and Esc can't be changed, and the height keys bring their Shift step of 10 with them. Tooltips and the list always show the keys as they are now.

Only your changes are saved, under `"Keys"` in `Settings.json`, by action name, with `""` for an action you've left without a key:

```json
"Keys": {
  "Grid": "Shift+G",
  "Axes": "G"
}
```

An entry Mapee can't read is skipped, and that action keeps its usual key.

# Command line

`mapee-cli` comes with Mapee: `mapee-cli.exe` beside `Mapee.exe` on Windows; on Linux the AppImage runs it when its first argument is a command (`Mapee.AppImage render ...`). It draws the way the app does, and needs no display, so it can run from a script or a scheduled task.

```
mapee-cli render <world> --out map.png [--dimension overworld|nether|end] [--area x1,z1,x2,z2] [--markup]
mapee-cli webmap <world> --out <folder> [--dimension overworld,nether] [--no-markers] [--force] [--full]
mapee-cli worlds          # the worlds in the game's world folders
mapee-cli styles          # the style ids
```

- `<world>` is a world folder, its `level.dat`, a `.mcworld`, or the name of a world that `mapee-cli worlds` lists.
- `render` writes one dimension at a pixel per block, transparent where nothing has been generated; `--area` keeps the blocks between two corners. It prints where the image lies in block coordinates.
- `webmap` writes the same web map as the export window, every dimension the world has. An earlier web map in the folder is replaced without asking, so a map can be kept up to date from a script; other files that would be replaced need `--force`. Over an earlier web map of the same world drawn the same way, only the regions the game has saved since are drawn, and the result line says how many (`Updated: Overworld: 3 of 26 regions changed`); `--full` draws everything.
- Both take `--style <id>` (default `default`), and `--slice <y>` or `--caves <y>` to draw as the height panel's Slice or Caves would.
- Markup: `render --markup` draws the pins and zones marked on the world in Mapee; `webmap` includes them unless `--no-markers` (which also leaves out the spawn and player). `--markup <file.json>` uses a file from the bar's Export instead.
- Exit codes: 0 done, 1 failed or nothing to draw, 2 wrong usage, 130 cancelled.

# Sample gui images

Default style
![0](https://user-images.githubusercontent.com/122542335/235355217-e7998e17-ae22-453c-830f-93ff0c32e0d7.png)

Y = 70, with gridlines enabled
![1](https://user-images.githubusercontent.com/122542335/235355320-830ce786-43a7-4c62-af82-6575050244a2.png)

The nether
![2](https://user-images.githubusercontent.com/122542335/235355329-bdb24aad-c416-413f-a2b5-c7eae53675db.png)

Cave view
![3](https://user-images.githubusercontent.com/122542335/235355338-1af6224f-cca3-4db5-aec4-8ee48bb1bb97.png)

Stone only block filter
![4](https://user-images.githubusercontent.com/122542335/235355355-f9479e9d-9958-4bdf-81c3-591920818f92.png)

Greenfield city in night mode
![5](https://user-images.githubusercontent.com/122542335/235355363-10000eb5-7c1c-4f00-a2d4-d75c314482d8.png)

Another example of a block filter
![6](https://user-images.githubusercontent.com/122542335/235355388-3364a0d1-13cf-41ff-b96d-514727abf642.png)

Heightmap
![7](https://user-images.githubusercontent.com/122542335/235355406-060dba26-b588-424c-9393-04395b3156eb.png)

Alpha
![8](https://user-images.githubusercontent.com/122542335/235355416-c25f0369-f831-48b1-b2ec-8cbfec8f124b.png)

# Sample render images

Y = 70
![y=70](https://user-images.githubusercontent.com/122542335/235524227-ce732b4b-7ae9-40d4-ac24-d1f175415a89.png)

'Default' style
![default](https://user-images.githubusercontent.com/122542335/235524257-3fd7ddad-aa21-4575-833d-0342a1391696.png)

Stone filter
![stone only](https://user-images.githubusercontent.com/122542335/235524297-b57f98d0-97ef-4590-86eb-44fb0ffaf7b0.png)

'Alpha' style
![alpha](https://user-images.githubusercontent.com/122542335/235532782-57796b66-7626-4171-a612-c39188fb4c46.png)

Heightmap
![heightmap](https://user-images.githubusercontent.com/122542335/235532968-69cf6ef3-b69b-437c-a057-2ae0db352526.png)

Cave view
![caves](https://user-images.githubusercontent.com/122542335/235532812-e44ed561-104c-4883-aac1-cca812fd92e8.png)

Granite, andesite and diorite filter
![igneous](https://user-images.githubusercontent.com/122542335/235532993-73f1946f-8d9d-4d55-9fb0-2d8d0e103da5.png)

Gravel filter
![gravel only](https://user-images.githubusercontent.com/122542335/235533082-e5dd8a95-78c4-4b10-8d8d-423105dff3be.png)

Nether
![nether](https://user-images.githubusercontent.com/122542335/235532849-36d3d858-ca3e-4c07-bf57-ea53ed02f2fd.png)

The End
![the_end](https://user-images.githubusercontent.com/122542335/235532871-16526e83-f1b7-4909-b0b7-6c4497785fbd.png)

Night mode
![night_0](https://user-images.githubusercontent.com/122542335/235532890-fd4e5b41-af51-499d-978c-2f519298d54d.png)

Greenfield city with night mode
![night_1](https://user-images.githubusercontent.com/122542335/235532913-9228f155-2c09-4901-b61c-ae5910f41390.png)

'Urban' style
![urban](https://user-images.githubusercontent.com/122542335/235532943-e665cc44-2d57-439f-a338-b877aa052d5a.png)
