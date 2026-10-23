using Mapper.Gui.Model;
using System.Collections.Generic;

namespace Mapper.Gui.Controller
{
    public class ToolButtonSegment : IToolButtonSegment
    {
        public IList<IToolButton> Tools { get; } = new List<IToolButton>();
    }
}
