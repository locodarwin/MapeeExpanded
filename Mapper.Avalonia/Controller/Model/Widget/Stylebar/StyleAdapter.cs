using Mapper.Gui.Model;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    public class StyleAdapter : IStyle
    {
        public string Name => BaseImplementation.Metadata.Name;
        public string Id => BaseImplementation.Metadata.Id;
        public IImage? Icon => BaseImplementation.Metadata.Icon;

        public Style BaseImplementation { get; }

        public StyleAdapter(Style baseImplementation) 
        {
            BaseImplementation = baseImplementation;
        }
    }
}
