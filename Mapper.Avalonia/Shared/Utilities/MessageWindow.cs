using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using System.Linq;
using System.Threading.Tasks;

namespace Mapper.Gui
{
    // A small OK box, or with Confirm a choice of two, standing in for WPF's MessageBox, which Avalonia doesn't have
    public class MessageWindow : Window
    {
        public MessageWindow(string message, string title) : this(message, title, null, null) { }

        private MessageWindow(string message, string title, string? yes, string? no)
        {
            Title = title;
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 35));
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MaxWidth = 500;

            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            if (yes is null || no is null)
            {
                buttons.Children.Add(CreateButton("OK", false));
            }
            else
            {
                buttons.Children.Add(CreateButton(yes, true));
                buttons.Children.Add(CreateButton(no, false));
            }

            Content = new StackPanel()
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock() { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238)) },
                    buttons
                }
            };
        }

        private Button CreateButton(string text, bool result)
        {
            Button button = new() { Content = text, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Classes.Add("dialog");
            button.Click += (sender, e) => Close(result);
            return button;
        }

        // True if the first choice was taken; closing the window is the second
        public static async Task<bool> Confirm(string message, string title, string yes, string no, Window owner)
        {
            return await new MessageWindow(message, title, yes, no).ShowDialog<bool?>(owner) == true;
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
