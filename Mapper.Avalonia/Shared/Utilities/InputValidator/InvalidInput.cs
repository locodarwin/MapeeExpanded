namespace Mapper.Gui
{
    public static class InvalidInput
    {
        public static bool ShowMessage(string message)
        {
            _ = MessageWindow.Show(message, "Input");
            return false;
        }
    }
}
