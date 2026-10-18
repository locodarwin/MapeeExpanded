using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using WorldEditor;

namespace Mapper.Gui.Controller
{
    // The height slider: cuts the map at a chosen Y, either as a slice of the current style or as caves.
    // Each dimension keeps its own cut. Dragging redraws only once the slider rests, since every change
    // redraws the whole map
    public class HeightWidget : IHeightWidget
    {
        public Scene Scene { get; }

        public bool Enabled => Scene.Domain.CurrentWorld is not null;
        public int MinY { get; private set; }
        public int MaxY { get; private set; } = 319;

        public HeightMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                Apply();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        public int Y
        {
            get => _y;
            set
            {
                value = Math.Clamp(value, MinY, MaxY);
                if (_y == value) return;
                _y = value;
                Changed?.Invoke(this, EventArgs.Empty);

                if (_mode == HeightMode.Surface) return;
                _delay.Stop();
                _delay.Start();
            }
        }

        public event EventHandler? Changed;

        private HeightMode _mode;
        private int _y;

        // The last height used in each dimension of this world, so turning the cut off and on again keeps it
        private readonly Dictionary<Dimension, int> _heights = [];
        private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(250) };

        public HeightWidget(Scene scene)
        {
            Scene = scene;

            _delay.Tick += (sender, e) =>
            {
                _delay.Stop();
                Apply();
            };

            Scene.WorldChanged += (old, current) =>
            {
                _heights.Clear();
                Read();
            };
            Scene.DimensionChanged += (old, current) => Read();
        }

        // Takes the state from the dimension now showing
        private void Read()
        {
            _delay.Stop();
            if (Scene.Domain.CurrentWorld is not WorldDomain world) return;

            DimensionDomain dimension = world.CurrentDimension;
            (MinY, MaxY) = HeightRange(world.Level, dimension.Dimension);

            if (dimension.Cut is HeightCut cut)
            {
                _mode = cut.Caves ? HeightMode.Caves : HeightMode.Slice;
                _y = cut.Y;
            }
            else
            {
                _mode = HeightMode.Surface;
                _y = _heights.TryGetValue(dimension.Dimension, out int y) ? y : DefaultHeight(dimension.Dimension);
            }
            _y = Math.Clamp(_y, MinY, MaxY);

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Apply()
        {
            _delay.Stop();
            if (Scene.Domain.CurrentWorld is not WorldDomain world) return;

            _heights[world.CurrentDimension.Dimension] = _y;
            Scene.SetCut(_mode == HeightMode.Surface ? null : new HeightCut((short)_y, _mode == HeightMode.Caves));
        }

        // Where the Caves style starts in this dimension: a height that finds caves in most worlds
        private int DefaultHeight(Dimension dimension)
        {
            Logic.Style? caves = Scene.Domain.Styles.FirstOrDefault(s => s.Metadata.Id == Scene.CavesStyleId);
            return caves?.GetProfile(dimension).HeightmapSettings.SetY ?? 60;
        }

        // The Nether's roof is at 127; Java worlds grew downwards and upwards in 1.18
        public static (int Min, int Max) HeightRange(Level level, Dimension dimension)
        {
            if (dimension == Dimension.Nether) return (0, 127);
            if (dimension == Dimension.TheEnd) return (0, 255);

            bool tall = level.Edition == Edition.Bedrock || level.Version.Version >= WorldEditor.Version.Rel_1_18;
            return tall ? (-64, 319) : (0, 255);
        }
    }
}
