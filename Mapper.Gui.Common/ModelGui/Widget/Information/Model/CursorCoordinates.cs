namespace Mapper.Gui.Model
{
    // The text of the label that rests beside the mouse over the map
    public static class CursorCoordinates
    {
        // Y is the block the map shows there, so it follows the height panel; without one there's no Y to give
        public static string Format(int x, int z, InspectedColumn? column)
        {
            return column is null ? $"X {x}   Z {z}" : $"X {x}   Y {column.Y}   Z {z}";
        }
    }
}
