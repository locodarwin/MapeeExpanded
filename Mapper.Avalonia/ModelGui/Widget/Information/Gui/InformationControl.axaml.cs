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
        }

        private void SetCursorSegmentVisibility(bool visible)
        {
            CursorHiddenLabel.Text = visible ? string.Empty : " None";

            CursorOverBlockLabel.Text = string.Empty;
            CursorOverChunkLabel.Text = string.Empty;

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
