using Avalonia.Media;

namespace Mapper.Gui.Model
{
    public interface IStyle
    {
        IImage? Icon { get; }
        string Name { get; }
        string Id { get; }
    }
}
