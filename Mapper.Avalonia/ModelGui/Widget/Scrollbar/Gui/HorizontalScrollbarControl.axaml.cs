using Mapper.Gui.Model;
using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Mapper.Gui
{
    public partial class HorizontalScrollbarControl : UserControl
    {
        public IScrollbarWidget Scrollbar { get; }

        private bool _preventScrollBarUpdate = false;

        public HorizontalScrollbarControl(IScrollbarWidget scrollbar)
        {
            InitializeComponent();

            Scrollbar = scrollbar;
            Scrollbar.Update += Scrollbar_Update;

            ScrollbarControl.ValueChanged += ScrollBar_ValueChanged;
            Loaded += (sender, e) => SetScrollbar();
        }

        private void ScrollBar_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_preventScrollBarUpdate) return;

            _preventScrollBarUpdate = true;
            Scrollbar.SetLeftMostVisiblePoint(e.NewValue);
            _preventScrollBarUpdate = false;
        }

        private void Scrollbar_Update(object? sender, EventArgs e)
        {
            _preventScrollBarUpdate = true;
            SetScrollbar();
            _preventScrollBarUpdate = false;
        }
        private void SetScrollbar()
        {
            ScrollbarUtilities.AdjustScrollbar(ScrollbarControl, Scrollbar, Bounds.Width);
        }
    }
}
