using System.Collections.Generic;

namespace Mapper.Gui.Model
{
    public interface IToolbarWidget
    {
        public IList<IToolButtonSegment> ToolButtonSegments { get; }
        // Labels under the icons where the window is wide enough; off, the buttons are icons only everywhere
        public bool ShowLabels { get; set; }
    }
}
