using System;
using System.Windows;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for AddBlock.xaml
    /// </summary>
    public partial class AddBlock : Window
    {
        public string NameResult { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        private bool _closing = false;

        public AddBlock(string name)
        {
            InitializeComponent();

            NameResult = name;
            NameTextBox.Text = name;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NameTextBox.Focus();
            NameTextBox.CaretIndex = NameTextBox.Text.Length;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            NameResult = NameTextBox.Text;

            DialogClosed = false;
            _closing = true;
            Close();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _closing = true;
            Close();
        }

        // However it closes (its own buttons, Esc, the frame's close button, or the app), losing focus on the way
        // out mustn't close it a second time, which WPF refuses with an exception
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closing = true;
            base.OnClosing(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            if (!_closing) Close();
        }
    }
}
