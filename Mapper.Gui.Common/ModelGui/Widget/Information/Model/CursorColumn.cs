namespace Mapper.Gui.Model
{
    // The block the mouse is over, as the map shows it; Column is null where there's nothing drawn
    public class CursorColumn
    {
        public int X { get; init; }
        public int Z { get; init; }
        public InspectedColumn? Column { get; init; }
    }
}
