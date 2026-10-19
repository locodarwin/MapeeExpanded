using System.Threading.Tasks;
using System.Windows;

namespace Mapper.Gui
{
    // A warning box for the shared code, which calls the Avalonia GUI's MessageWindow by the same name
    public static class MessageWindow
    {
        public static Task Show(string message, string title, Window? owner = null)
        {
            if (owner is null) MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            else MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

            return Task.CompletedTask;
        }
    }
}
