using Mapper.Gui.Model;
using System;
using System.Buffers;
using System.Collections.Generic;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    public class RegionLoader : IRegionLoader
    {
        public Scene Scene { get; }
        public WorldMapper WorldMapper { get; }
        public ILoadPattern LoadPattern { get; set; }

        public XzRange LoadedArea => _currentScene is null ? XzRange.Empty : new(_currentScene.TopLeft, _currentScene.BottomRight);

        private readonly IDictionary<Dimension, SceneCache> _cachedScenes;
        private SceneCache? _currentScene;
        private bool _inSceneChangingProcess = false;
        private object _lock = new();
        private int _added = 0;

        // Regions of the dimension on show to draw again because the world changed under them, each with whether its
        // new drawing has been queued yet: a drawing that was already under way when the change came in shows the old
        // world, so only one queued afterwards replaces the picture
        private readonly Dictionary<XzPoint, bool> _stale = [];

        public RegionLoader(Scene scene)
        {
            Scene = scene;
            WorldMapper = new WorldMapper(Scene.Domain.ChunkMapperPack);
            LoadPattern = new SpiralLoadPattern();
            _cachedScenes = new Dictionary<Dimension, SceneCache>();

            if (Scene.Domain.CurrentWorld is not null)
            {
                SetCurrentScene(Scene.Domain.CurrentWorld, Scene.Domain.CurrentWorld.CurrentDimension);
            }

            WorldMapper.RegionRendered += OnRegionRendered;
            Scene.WorldBeginChange += Scene_WorldBeginChange;
            Scene.DimensionBeginChange += Scene_DimensionBeginChange;
            Scene.WorldChanged += Scene_WorldChanged;
            Scene.DimensionChanged += Scene_DimensionChanged;
            Scene.StyleReset += Scene_StyleReset;
            Scene.ProfileReset += Scene_ProfileReset;
        }

        private void Scene_WorldBeginChange(WorldDomain? old, WorldDomain current)
        {
            ForgetStale();
            if (old is not null)
            {
                _inSceneChangingProcess = true;
                WorldMapper.Invoke(WorldMapper.Stop);

                _cachedScenes.Clear();
                _currentScene = null;
            }

            SetCurrentScene(current, current.CurrentDimension);
        }
        private void Scene_DimensionBeginChange(DimensionDomain old, DimensionDomain current)
        {
            ForgetStale();
            _inSceneChangingProcess = true;
            WorldMapper.Invoke(WorldMapper.Stop);

            if (Scene.Domain.CurrentWorld is null) return;
            SetCurrentScene(Scene.Domain.CurrentWorld, current);
        }
        private void Scene_WorldChanged(WorldDomain? old, WorldDomain current)
        {
            WorldMapper.Invoke(() =>
            {
                WorldMapper.SetScene(current.CurrentDimension.Scene.SceneParameter);
                _inSceneChangingProcess = false;
            });
        }
        private void Scene_DimensionChanged(DimensionDomain old, DimensionDomain current)
        {
            WorldMapper.Invoke(() =>
            {
                WorldMapper.SetScene(current.Scene.SceneParameter);
                _inSceneChangingProcess = false;
            });
        }

        private void Scene_StyleReset(Style old, Style current)
        {
            ForgetStale();
            if (Scene.Domain.CurrentWorld is null) return;

            foreach (DimensionDomain domain in Scene.Domain.CurrentWorld.Dimensions)
            {
                domain.Scene.RenderedRegions.Clear();
            }
            _cachedScenes.Clear();
        }
        private void Scene_ProfileReset(DimensionDomain dimension)
        {
            if (_currentScene is null || Scene.Domain.CurrentWorld is null) return;

            _inSceneChangingProcess = true;
            ForgetStale();

            SetCurrentScene(Scene.Domain.CurrentWorld, dimension);
            _currentScene.Reset();

            WorldMapper.Invoke(() =>
            {
                dimension.Scene.RenderedRegions.Clear();
                WorldMapper.SetScene(dimension.Scene.SceneParameter);
                _inSceneChangingProcess = false;
            });
        }

        // Draws these regions of the dimension on show again, each picture staying until its new one is ready. Safe
        // from any thread; the regions are queued with the next look at what's visible
        public void Refresh(IEnumerable<Coords> regions)
        {
            lock (_lock)
            {
                foreach (Coords region in regions) _stale[new XzPoint(region.X, region.Z)] = false;
            }
            if (_currentScene is SceneCache scene) scene.PrevRange = XzRange.Empty;
        }

        private void ForgetStale()
        {
            lock (_lock) _stale.Clear();
        }

        private void SetCurrentScene(WorldDomain world, DimensionDomain dimension)
        {
            if (!_cachedScenes.TryGetValue(dimension.Dimension, out SceneCache? scene) || scene is null)
            {
                scene = new SceneCache();
                _cachedScenes.Add(dimension.Dimension, scene);
            }

            Profile profile = Scene.GetDrawingProfile(world, dimension, out ScanType scanType);
            Scene.Domain.ChunkMapperPack.UpdatePack(profile, scanType);

            scene.PrevRange = XzRange.Empty;
            _currentScene = scene;
        }

        private void OnRegionRendered(Coords coords, ICanvas canvas)
        {
            if (_currentScene is null || Scene.Domain.CurrentWorld is null || _inSceneChangingProcess)
            {
                if (StartupLog.Active) StartupLog.Mark($"  region {coords.X},{coords.Z} rendered but dropped: {(_currentScene is null ? "no scene" : Scene.Domain.CurrentWorld is null ? "no world" : "scene changing")}");
                return;
            }
            bool replace = TakeStale(coords);
            if (!replace && !IsSafeToAdd(coords.X, coords.Z))
            {
                if (StartupLog.Active) StartupLog.Mark($"  region {coords.X},{coords.Z} rendered but already there");
                return;
            }

            var bitmap = CanvasBitmap.Create(canvas);
            if (bitmap is not null)
            {
                Coords topLeft = new(canvas.TopLeftPoint.X - (canvas.PixelWidth - 1), canvas.TopLeftPoint.Z);
                Coords bottomRight = new(topLeft.X + canvas.PixelWidth - 1, topLeft.Z + canvas.PixelHeight - 1);

                if (topLeft.X < _currentScene.TopLeft.X) _currentScene.TopLeft = new XzPoint(topLeft.X, _currentScene.TopLeft.Z);
                if (topLeft.Z < _currentScene.TopLeft.Z) _currentScene.TopLeft = new XzPoint(_currentScene.TopLeft.X, topLeft.Z);

                if (bottomRight.X > _currentScene.BottomRight.X) _currentScene.BottomRight = new XzPoint(bottomRight.X, _currentScene.BottomRight.Z);
                if (bottomRight.Z > _currentScene.BottomRight.Z) _currentScene.BottomRight = new XzPoint(_currentScene.BottomRight.X, bottomRight.Z);

                XzRange areaInRegion = new(
                    MathUtilities.NegMod(topLeft.X, 512),
                    MathUtilities.NegMod(topLeft.Z, 512),
                    canvas.PixelWidth - 1,
                    canvas.PixelHeight - 1);

                RenderedRegion output = new(new XzPoint(coords.X, coords.Z), areaInRegion, bitmap);
                AddSafely(output, replace);
            }
            else
            {
                AddSafely(new RenderedRegion(new XzPoint(coords.X, coords.Z), null, null), replace);
            }
        }

        // Whether this drawing is the one a refresh asked for
        private bool TakeStale(Coords coords)
        {
            XzPoint point = new(coords.X, coords.Z);
            lock (_lock)
            {
                if (!_stale.TryGetValue(point, out bool queued) || !queued) return false;
                _stale.Remove(point);
                return true;
            }
        }

        private bool IsSafeToAdd(int x, int z)
        {
            if(Scene.Domain.CurrentWorld is null) return false;

            lock (_lock)
            {
                return !Scene.Domain.CurrentWorld.CurrentDimension.Scene.RenderedRegions.ContainsKey(new XzPoint(x, z));
            }
        }
        // replace: the region is being drawn again, so its new picture takes the old one's place
        private void AddSafely(IRenderedRegion region, bool replace = false)
        {
            if (Scene.Domain.CurrentWorld is null) return;

            lock (_lock)
            {
                var regions = Scene.Domain.CurrentWorld.CurrentDimension.Scene.RenderedRegions;
                if (regions.ContainsKey(region.Coords))
                {
                    if (!replace) return;
                    regions[region.Coords] = region;
                }
                else
                {
                    regions.Add(region.Coords, region);
                }
                _added++;
            }
            if (StartupLog.Active && (_added <= 10 || _added % 25 == 0))
            {
                StartupLog.Mark($"  region {region.Coords.X},{region.Coords.Z} added to {Scene.Domain.CurrentWorld.CurrentDimension.Dimension.Namespace} " +
                    $"({(region.Image is null ? "empty" : "with image")}), {_added} added so far");
            }
        }

        public void LoadArea(XzRange range)
        {
            if (_currentScene is null || range == _currentScene?.PrevRange || _inSceneChangingProcess)
            {
                if (StartupLog.Active) StartupLog.MarkChange("load", $"Load area {StartupLog.Describe(range)} skipped: {(_currentScene is null ? "no scene" : _inSceneChangingProcess ? "scene changing" : "same as before")}");
                return;
            }

            WorldMapper.Invoke(() =>
            {
                Coords[] queue = ArrayPool<Coords>.Shared.Rent((int)range.Size.X * (int)range.Size.Z);
                Memory<Coords> queueMemory = queue;
                CreateQueue(range, ref queueMemory);
                if (StartupLog.Active) StartupLog.Mark($"Load area {StartupLog.Describe(range)}: {queueMemory.Length} regions queued");

                WorldMapper.Queue.ReplaceWith(queueMemory);
                ArrayPool<Coords>.Shared.Return(queue);
            });

            if (_currentScene is not null) _currentScene.PrevRange = range;
        }
        private void CreateQueue(XzRange range, ref Memory<Coords> queue)
        {
            if (Scene.Domain.CurrentWorld is null) return;
            IRenderedScene renderedScene = Scene.Domain.CurrentWorld.CurrentDimension.Scene;

            int index = 0;
            Span<Coords> queueSpan = queue.Span;
            foreach (XzPoint regionPoint in LoadPattern.CreatePattern(range))
            {
                Coords regionCoords = regionPoint.ToCoords();

                if (!renderedScene.SceneParameter.RegionStore.Exists(regionCoords))
                {
                    continue;
                }
                // Drawn already, unless the world has changed there since
                XzPoint point = new(regionCoords.X, regionCoords.Z);
                lock (_lock)
                {
                    if (_stale.ContainsKey(point)) _stale[point] = true;
                    else if (renderedScene.RenderedRegions.ContainsKey(point)) continue;
                }

                queueSpan[index++] = regionCoords;
            }

            queue = queue[..index];
        }
    }
}