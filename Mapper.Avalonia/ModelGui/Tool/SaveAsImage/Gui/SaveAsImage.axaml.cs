using Mapper.Gui.Model;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Mapper.Gui
{
    public partial class SaveAsImageControl : Window
    {
        private readonly IImageSaver _imageSaver;
        private readonly DispatcherTimer _savingAnimationTimer;
        private int _savingDotCount;
        private bool _isSaving;

        public SaveAsImageControl(IImageSaver imageSaver)
        {
            InitializeComponent();

            _imageSaver = imageSaver;

            _savingAnimationTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _savingAnimationTimer.Tick += SavingAnimationTimer_Tick;

            ScreenshotRadioButton.IsCheckedChanged += (sender, e) =>
            {
                if (ScreenshotRadioButton.IsChecked != true) return;
                SetScreenshotDescritpion();
                SetFullResolutionPanelEnabled(false);
            };
            FullResolutionButton.IsCheckedChanged += (sender, e) =>
            {
                if (FullResolutionButton.IsChecked != true) return;
                SetFullScreenResolutionDescritpion();
                SetFullResolutionPanelEnabled(true);
            };
            ClipAreaCheckBox.IsCheckedChanged += (sender, e) => SetFullResolutionLabel();

            SetScreenshotDescritpion();
            SetFullResolutionPanelEnabled(false);

            SetScreenshotLabel();
            SetFullResolutionLabel();
        }

        private void SetScreenshotLabel() 
        {
            Size size = _imageSaver.GetScreenshotSize();
            ScreenshotRadioButton.Content = $"Screenshot  ({size.Width}x{size.Height})";
        }
        private void SetFullResolutionLabel()
        {
            Size size = _imageSaver.GetFullResolutionSize(CreateFullResolutionImageArgs());
            FullResolutionButton.Content = $"Full resolution  ({size.Width}x{size.Height})";
        }

        private void SetFullResolutionPanelEnabled(bool enabled)
        {
            FullResolutionPanel.Opacity = enabled ? 1 : 0;
            FullResolutionPanel.IsHitTestVisible = enabled;
        }

        private void SetScreenshotDescritpion()
        {
            string description = "Captures the screen. Some image scaling artifacts may be present.";
            DescriptionLabel.Text = $"Description: {description}";
        }
        private void SetFullScreenResolutionDescritpion()
        {
            string description = "Saves the image as one pixel per block. Gridlines will note be visible. Image size can be rather large.";
            DescriptionLabel.Text = $"Description: {description}";
        }

        private async void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_isSaving) return;

            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                Title = "Save as image",
                SuggestedFileName = "RenderedMap",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("Image files (.png)") { Patterns = ["*.png"] }]
            });

            string? path = file?.TryGetLocalPath();
            if (path is null) return;

            bool saveScreenshot = ScreenshotRadioButton.IsChecked is not null && ScreenshotRadioButton.IsChecked.Value;
            FullResolutionImageArgs fullResolutionImageArgs = CreateFullResolutionImageArgs();
            Exception? saveException = null;

            BeginSaving();

            try
            {
                if (saveScreenshot)
                {
                    _imageSaver.SaveAsScreenshot(path);
                }
                else
                {
                    await Task.Run(() => _imageSaver.SaveAsFullResolution(path, fullResolutionImageArgs));
                }
            }
            catch (Exception exception)
            {
                saveException = exception;
            }
            finally
            {
                EndSaving();
            }

            if (saveException is not null)
            {
                await MessageWindow.Show($"Image could not be saved.\n\n{saveException.Message}", "Save as image", this);
                return;
            }

            Close();
        }

        private void BeginSaving()
        {
            _isSaving = true;
            _savingDotCount = 0;
            SavingLabel.Text = "Saving";
            SavingLabel.IsVisible = true;
            SetControlsEnabled(false);
            _savingAnimationTimer.Start();
        }

        private void EndSaving()
        {
            _savingAnimationTimer.Stop();
            SavingLabel.IsVisible = false;
            SetControlsEnabled(true);
            _isSaving = false;
        }

        private void SavingAnimationTimer_Tick(object? sender, EventArgs e)
        {
            _savingDotCount = (_savingDotCount + 1) % 4;
            SavingLabel.Text = $"Saving{new string('.', _savingDotCount)}";
        }

        private void SetControlsEnabled(bool enabled)
        {
            ScreenshotRadioButton.IsEnabled = enabled;
            FullResolutionButton.IsEnabled = enabled;
            ClipAreaCheckBox.IsEnabled = enabled;
            CheckerPatternRadioBox.IsEnabled = enabled;
            BlackColorRadioBox.IsEnabled = enabled;
            InvisibleColorRadioBox.IsEnabled = enabled;
            SaveButton.IsEnabled = enabled;
        }

        private FullResolutionImageArgs CreateFullResolutionImageArgs() 
        {
            return new FullResolutionImageArgs()
            {
                ClipArea = ClipAreaCheckBox.IsChecked is not null && ClipAreaCheckBox.IsChecked.Value,
                CheckerPatternEnabled = CheckerPatternRadioBox.IsChecked is not null && CheckerPatternRadioBox.IsChecked.Value,
                BackgroundColor = BlackColorRadioBox.IsChecked is not null && BlackColorRadioBox.IsChecked.Value ? Colors.Black : Color.FromArgb(0, 0, 0, 0)
            };
        }
    }
}
