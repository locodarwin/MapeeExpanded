using System.Collections.Generic;

namespace Mapper.Gui.Model
{
    // A group of buttons that belong together; the toolbar draws a line between groups
    public interface IToolButtonSegment
    {
        public IList<IToolButton> Tools { get; }
    }
}
