using Mapper.Gui.Model;
using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Mapper.Gui
{
    public partial class InformationControl : UserControl
    {
        public IInformationWidget Information { get; }

        private TimedUpdater _timedInfoUpdater = new TimedUpdater();
        private TimedUpdater _timedCursorUpdater = new TimedUpdater();

        private SceneInformation? _lastInformation;
        private XzPoint _lastCursorPoint;

        public InformationControl(IInformationWidget information)
        {
            InitializeComponent();
            InitializeBackgrounWork();

            Information = information;
            Information.InformationUpdate += Information_InformationUpdate;
            Information.CursorUpdate += Information_CursorUpdate;
            Information.ColumnUpdate += Information_ColumnUpdate;
        }

        // Catches up on updates the timed updaters skipped. A UI-thread timer rather than the WPF GUI's background
        // thread, since Avalonia controls can't be read from other threads
        private void InitializeBackgrounWork() 
        {
            DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (sender, e) =>
            {
                if (!IsVisible) return;

                if (_timedInfoUpdater.PreviousSkipped && _lastInformation is not null)
                {
                    SetInformationLabels(_lastInformation);
                }

                if (_timedCursorUpdater.PreviousSkipped)
                {
                    SetCursorLabels(_lastCursorPoint);
                }
            };
            timer.Start();
        }

        private void Information_InformationUpdate(object? sender, SceneInformation information) 
        {
            _lastInformation = information;
            _timedInfoUpdater.Update(() =>
            {
                SetInformationLabels(information);
            });
        }
        private void Information_CursorUpdate(object? sender, XzPoint point)
        {
            _lastCursorPoint = point;
            _timedCursorUpdater.Update(() => SetCursorLabels(point));
        }

        private void SetInformationLabels(SceneInformation information) 
        {
            LoadedRegionsLabel.Text = information.LoadedRegions.ToString();

            if (information.LoadedArea is not null)
            {
                string text;
                if (information.LoadedRegions == 0) text = "Empty";
                else text = SizeToString(information.LoadedArea.Value.Size);

                LoadedSizeLabel.Text = text;
            }

            VisibleAreaTopLeftLabel.Text = information.VisibleArea.TopLeftPoint.ToString("N0");
            VisibleAreaBottomRightLabel.Text = information.VisibleArea.BottomRightPoint.ToString("N0");
            VisibleAreaSizeLabel.Text = SizeToString(information.VisibleArea.Size);

            if (information.WorldInformation is not null)
            {
                WorldTextBlock.Opacity = 1;

                WorldNameLabel.Text = information.WorldInformation.WorldName;
                WorldVersionLabel.Text = information.WorldInformation.Version.VersionName;
                LiveLabel.Text = information.LiveNote is string note ? "; " + note : string.Empty;
            }
            else
            {
                WorldTextBlock.Opacity = 0;
            }

            static string SizeToString(XzPoint size)
            {
                return $"{(int)size.X}x{(int)size.Z}";
            };
        }
        private void SetCursorLabels(XzPoint point) 
        {
            if (Information.MainControl is null || !Information.MainControl.IsVisible) return;

            SetCursorSegmentVisibility(Information.MainControl.IsMouseOver);
            if (!Information.MainControl.IsMouseOver) return;

            CursorOverBlockLabel.Text = point.ToString("N0");
            CursorOverChunkLabel.Text = BlockToChunk(point).ToString("N0");

            // Where a portal here would lead: the place in the other dimension
            string? across = CursorAcross.Prefix(Information.Dimension);
            CursorAcrossLabel.Text = across ?? string.Empty;
            CursorAcrossPlaceLabel.Text = across is null ? string.Empty : CursorAcross.Place(Information.Dimension!.Value, point).ToString("N0");
            CursorAcrossEndLabel.Text = across is null ? string.Empty : ")";
        }

        // "; Grass Block at Y 64, Plains", or for water, "; Water 5 deep over Sand at Y 62, Ocean"
        private void Information_ColumnUpdate(object? sender, CursorColumn cursor)
        {
            if (cursor.Column is not InspectedColumn column || !Information.MainControl.IsMouseOver)
            {
                SetColumnLabels(null, null, null);
                return;
            }

            string block = ColumnInspector.DisplayName(column.Block.Name);
            if (column.Depth > 0)
            {
                block += $" {column.Depth} deep";
                if (column.Floor is WorldEditor.Block floor) block += $" over {ColumnInspector.DisplayName(floor.Name)}";
            }

            SetColumnLabels(block, column.Y.ToString(), column.Biome is string biome ? ColumnInspector.DisplayName(biome) : null);
        }
        private void SetColumnLabels(string? block, string? height, string? biome)
        {
            ColumnBlockLabel.Text = block is null ? string.Empty : $"; {block}";
            ColumnAtLabel.Text = height is null ? string.Empty : " at Y ";
            ColumnHeightLabel.Text = height ?? string.Empty;
            ColumnBiomeSeparatorLabel.Text = biome is null ? string.Empty : ", ";
            ColumnBiomeLabel.Text = biome ?? string.Empty;
        }

        private void SetCursorSegmentVisibility(bool visible)
        {
            CursorHiddenLabel.Text = visible ? string.Empty : " None";

            CursorOverBlockLabel.Text = string.Empty;
            CursorOverChunkLabel.Text = string.Empty;
            CursorAcrossLabel.Text = string.Empty;
            CursorAcrossPlaceLabel.Text = string.Empty;
            CursorAcrossEndLabel.Text = string.Empty;

            if (!visible) SetColumnLabels(null, null, null);

            CursorBeginLabel.Text = visible ? " (" : string.Empty;
            CursorMiddleLabel.Text = visible ? ") in chunk: (" : string.Empty;
            CursorEndLabel.Text = visible ? ")" : string.Empty;
        }
        private static XzPoint BlockToChunk(XzPoint block)
        {
            int x = (int)block.X / 16, z = (int)block.Z / 16;

            if (block.X < 0) x--;
            if (block.Z < 0) z--;

            return new XzPoint(x, z);
        }
    }
}
