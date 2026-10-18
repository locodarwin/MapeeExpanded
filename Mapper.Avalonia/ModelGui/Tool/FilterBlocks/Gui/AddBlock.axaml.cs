using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Mapper.Gui
{
    // Asks for a block name, in a flyout under the add button; the WPF GUI uses a borderless window
    public partial class AddBlock : UserControl
    {
        public string NameResult { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        public event EventHandler? Finished;

        public AddBlock(string name)
        {
            InitializeComponent();

            NameResult = name;
            NameTextBox.Text = name;

            NameTextBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter) AddButton_Click(sender, e);
            };
            AttachedToVisualTree += (sender, e) =>
            {
                NameTextBox.Focus();
                NameTextBox.CaretIndex = NameTextBox.Text?.Length ?? 0;
            };
        }

        private void AddButton_Click(object? sender, RoutedEventArgs e)
        {
            NameResult = NameTextBox.Text ?? string.Empty;

            DialogClosed = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }
        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }
}
