using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using System.Linq;
using System.Threading.Tasks;

namespace Mapper.Gui
{
    // A small OK box, standing in for WPF's MessageBox, which Avalonia doesn't have
    public class MessageWindow : Window
    {
        public MessageWindow(string message, string title)
        {
            Title = title;
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 35));
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MaxWidth = 500;

            Button ok = new() { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center };
            ok.Classes.Add("dialog");
            ok.Click += (sender, e) => Close();

            Content = new StackPanel()
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock() { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238)) },
                    ok
                }
            };
        }

        public static Task Show(string message, string title, Window? owner = null)
        {
            owner ??= ActiveWindow();

            MessageWindow window = new(message, title);
            if (owner is null)
            {
                window.Show();
                return Task.CompletedTask;
            }

            return window.ShowDialog(owner);
        }

        // The window the user is working in, for dialogs to sit on top of
        public static Window? ActiveWindow()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return null;
            return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
        }
    }
}
