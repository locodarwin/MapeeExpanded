using Mapper.Gui.Model;
using AssetSystem.Biome;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldEditor;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
#endif

namespace Mapper.Gui.Logic
{
    public class Scene
    {
        public ProgramDomain Domain { get; }
        public Map Map { get; }
        public RegionLoader RegionLoader { get; }

        public event WorldChangedEventHandler? WorldBeginChange;
        public event WorldChangedEventHandler? WorldChanged;

        public event DimensionChangedEventHandler? DimensionBeginChange;
        public event DimensionChangedEventHandler? DimensionChanged;

        public event StyleResetEventHandler? StyleBeginReset;
        public event StyleResetEventHandler? StyleReset;
        public event ProfileResetEventHandler? ProfileReset;

        public Scene(CanvasControl outputControl, ProgramDomain domain)
        {
            Domain = domain;

            RegionLoader = new RegionLoader(this);
            Map = new Map(outputControl, RegionLoader);
        }

        public void SetWorld(Level level)
        {
            DetectAlphaWorld(level);

            SceneInfo sceneParameter = new(level, Dimension.Overworld);
            DimensionDomain dimension = new(new RenderedScene(sceneParameter))
            {
                Dimension = sceneParameter.Dimension
            };

            WorldDomain worldDomain = new(Domain.CurrentStyle.Clone(), level, dimension);
            LoadDataPacks(level, worldDomain);

            Profile profile = Domain.CurrentStyle.GetProfile(dimension.Dimension);
            SetDimensionProfile(dimension, profile);

            worldDomain.CurrentDimension.Scaling.CenterPoint = new Point(level.Player.Position.X, level.Player.Position.Z);

            WorldDomain? old = Domain.CurrentWorld;
            WorldBeginChange?.Invoke(old, worldDomain);

            ClearsWorld();
            Domain.CurrentWorld = worldDomain;

            WorldChanged?.Invoke(old, Domain.CurrentWorld);
        }
        // A world from before Beta 1.2 has no version; its chunks in c.<x>.<z>.dat files give it away
        public static void DetectAlphaWorld(Level level)
        {
            if (level.Version.Version != WorldEditor.Version.Unknown) return;

            bool hasAlphaChunks = Directory.EnumerateFiles(level.Directory ?? "", "c.*.*.dat", SearchOption.AllDirectories).Any();
            if (hasAlphaChunks)
            {
                level.Version = new LevelVersion()
                {
                    Version = WorldEditor.Version.Pre_Beta_1_2
                };
            }
        }
        private void ClearsWorld()
        {
            if (Domain.CurrentWorld is null) return;
            Domain.CurrentWorld = null;
        }

        private void LoadDataPacks(Level level, WorldDomain world) 
        {
            DataPackAssetPack? assetPack = ReadDataPacks(level, Domain.TexturePack.AssetPack.Colormap);
            if (assetPack is null) return;

            world.DataPack = assetPack;
            if (!world.Style.Metadata.DisableDatapackStyle) UseDataPackColors(world.Style, assetPack);
        }

        // The colours the world's data packs give their biomes, or null when it has none that load
        public static DataPackAssetPack? ReadDataPacks(Level level, Colormap colormap)
        {
            DataPackReader reader = new();
            IList<IDataPack> dataPacks = new List<IDataPack>();

            foreach (string datapackFileName in level.DataPacks) 
            {
                string path = Path.Combine(level.Directory ?? "", "datapacks", Path.GetFileName(datapackFileName));

                try
                {
                    DataPack? dataPack = reader.Read(path);
                    if (dataPack is not null) dataPacks.Add(dataPack);
                }
                catch { }
            }

            DataPackAssetPackFactory factory = new DataPackAssetPackFactory();
            return factory.Create(new DataPackAssetPackFactoryArgs() 
            {
                DataPacks = dataPacks,
                Colormap = colormap
            });
        }

        // Colours the data packs' biomes in the style, leaving the style's own colours for the rest
        public static void UseDataPackColors(Style style, DataPackAssetPack dataPack) 
        {
            CompositeBiomeAsset<VecRgb> asset = new(dataPack.BiomeColorAsset, style.AssetPack.BiomeColorAsset)
            {
                DefaultBiome = style.AssetPack.BiomeColorAsset.DefaultBiome,
                DefaultOutput = style.AssetPack.BiomeColorAsset.DefaultOutput
            };

            style.AssetPack.BiomeColorAsset = asset;
        }

        public void ChangeDimension(Dimension dimension)
        {
            if (Domain.CurrentWorld is null || Domain.CurrentWorld.CurrentDimension.Dimension == dimension) return;

            DimensionDomain? dimensionDomain = Domain.CurrentWorld.GetDimension(dimension);
            if (dimensionDomain is null)
            {
                dimensionDomain = new DimensionDomain(new RenderedScene(new SceneInfo(Domain.CurrentWorld.Level, dimension)))
                {
                    Dimension = dimension
                };

                Profile profile = Domain.CurrentStyle.GetProfile(dimensionDomain.Dimension);
                SetDimensionProfile(dimensionDomain, profile);

                Domain.CurrentWorld.SetDimension(dimensionDomain);
            }

            DimensionDomain old = Domain.CurrentWorld.CurrentDimension;
            StartupLog.Mark($"Dimension changed to {dimension.Namespace}");

            DimensionBeginChange?.Invoke(old, dimensionDomain);

            Domain.CurrentWorld.CurrentDimension = dimensionDomain;
            DimensionChanged?.Invoke(old, dimensionDomain);
        }

        public void UpdateStyle(Style style) 
        {
            StartupLog.Mark($"Style changed to {style.Metadata.Id}");
            Style old = Domain.CurrentStyle;

            Domain.CurrentStyle = style;
            if (Domain.CurrentWorld is null) return;

            style = style.Clone();

            StyleBeginReset?.Invoke(old, style);

            Domain.CurrentWorld.Style = style;
            foreach (DimensionDomain dimension in Domain.CurrentWorld.Dimensions) 
            {
                SetDimensionProfile(dimension, style.GetProfile(dimension.Dimension));
            }

            if (!style.Metadata.DisableDatapackStyle && Domain.CurrentWorld.DataPack is DataPackAssetPack dataPack) UseDataPackColors(style, dataPack);

            StyleReset?.Invoke(old, Domain.CurrentStyle);
            ProfileReset?.Invoke(Domain.CurrentWorld.CurrentDimension);
        }
        public void UpdateProfile(Profile profile)
        {
            StartupLog.Mark("Profile changed (block filter or render settings)");
            if (Domain.CurrentWorld is null) return;

            if (!ReferenceEquals(Domain.CurrentWorld.Style.AssetPack, profile.AssetPack)) Domain.CurrentWorld.Style.AssetRevision = Guid.NewGuid().ToString("N");
            Domain.CurrentWorld.Style.AssetPack = profile.AssetPack;
            SetDimensionProfile(Domain.CurrentWorld.CurrentDimension, profile);

            ProfileReset?.Invoke(Domain.CurrentWorld.CurrentDimension);
        }

        // Cuts the current dimension's map at a height, or with null, goes back to the style's own view
        public void SetCut(HeightCut? cut)
        {
            if (Domain.CurrentWorld is null || Domain.CurrentWorld.CurrentDimension.Cut == cut) return;

            Domain.CurrentWorld.CurrentDimension.Cut = cut;
            StartupLog.Mark($"Height cut changed to {cut?.ToString() ?? "none"}");
            ProfileReset?.Invoke(Domain.CurrentWorld.CurrentDimension);
        }

        // What the map is drawn with: the dimension's own profile, or under a cut, that profile (or the Caves
        // style's, for cave mode) started at the cut's height
        public Profile GetDrawingProfile(WorldDomain world, DimensionDomain dimension, out ScanType scanType)
        {
            scanType = world.Style.ScanType;
            Profile profile = new(world.Style.AssetPack, dimension.HeightmapSettings, dimension.RenderSettings);
            if (dimension.Cut is not HeightCut cut) return profile;

            return Cut(profile, dimension.Dimension, cut, Domain.Styles, ref scanType);
        }

        // The profile started at the cut's height; for cave mode, the Caves style's profile (when there is one)
        public static Profile Cut(Profile profile, Dimension dimension, HeightCut cut, IEnumerable<Style> styles, ref ScanType scanType)
        {
            if (cut.Caves)
            {
                scanType = ScanType.Cave;
                if (styles.FirstOrDefault(s => s.Metadata.Id == CavesStyleId) is Style caves) profile = caves.GetProfile(dimension);
            }

            HeightmapSettings heightmap = profile.HeightmapSettings;
            heightmap.HeightmapType = HeightmapType.SetY;
            heightmap.SetY = cut.Y;

            return new Profile(profile.AssetPack, heightmap, profile.RenderSettings);
        }

        // For a dimension not yet opened, the style's own view of it
        public Profile GetDrawingProfile(WorldDomain world, Dimension dimension, out ScanType scanType)
        {
            if (world.GetDimension(dimension) is DimensionDomain domain) return GetDrawingProfile(world, domain, out scanType);

            scanType = world.Style.ScanType;
            Profile profile = world.Style.GetProfile(dimension);
            return new Profile(world.Style.AssetPack, profile.HeightmapSettings, profile.RenderSettings);
        }

        // Where a column is looked at from, and whether as a cave, matching how the map is drawn
        public (int? StartY, bool Caves) GetScanStart(WorldDomain world, DimensionDomain dimension)
        {
            Profile profile = GetDrawingProfile(world, dimension, out ScanType scanType);
            int? startY = profile.HeightmapSettings.HeightmapType == HeightmapType.SetY ? profile.HeightmapSettings.SetY : null;
            return (startY, scanType == ScanType.Cave);
        }

        public const string CavesStyleId = "caves";

        private void SetDimensionProfile(DimensionDomain dimension, Profile profile) 
        {
            dimension.HeightmapSettings = profile.HeightmapSettings;
            dimension.RenderSettings = profile.RenderSettings;
        }
    }
}
