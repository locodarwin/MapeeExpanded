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

        // True for Yes. The message box has its own Yes and No, so the Avalonia GUI's button names go unused here
        public static Task<bool> Confirm(string message, string title, string yes, string no, Window owner)
        {
            return Task.FromResult(MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        }
    }
}
