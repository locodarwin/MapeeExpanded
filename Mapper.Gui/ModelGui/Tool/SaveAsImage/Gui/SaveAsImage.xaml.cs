using Mapper.Gui.Model;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for SaveAsImage.xaml
    /// </summary>
    public partial class SaveAsImageControl : Window
    {

        private readonly IImageSaver _imageSaver;
        private readonly DispatcherTimer _savingAnimationTimer;
        private int _savingDotCount;
        private bool _isSaving;

        // Set while a web map exports; the save button cancels it
        private CancellationTokenSource? _webMapCancellation;

        public SaveAsImageControl(IImageSaver imageSaver)
        {
            InitializeComponent();

            _imageSaver = imageSaver;

            _savingAnimationTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _savingAnimationTimer.Tick += SavingAnimationTimer_Tick;

            Closing += (sender, e) => _webMapCancellation?.Cancel();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = new CustomFrameWindowInitializer(this, Template);

            if (ScreenshotRadioButton.IsChecked is not null && ScreenshotRadioButton.IsChecked.Value)
            {
                SetScreenshotDescritpion();
                SetFullResolutionPanelEnabled(false);
            }
            else
            {
                SetFullScreenResolutionDescritpion();
                SetFullResolutionPanelEnabled(false);
            }

            SetScreenshotLabel();
            SetFullResolutionLabel();
            WebMapButton.IsEnabled = _imageSaver.CanSaveWebMap;
            WebMapDescriptionLabel.Text = _imageSaver.WebMapDescription;
        }

        private void SetScreenshotLabel() 
        {
            Size size = _imageSaver.GetScreenshotSize();
            ScreenshotRadioButton.Content = $"Screenshot  ({size.Width}x{size.Height})";
        }
        private void SetFullResolutionLabel()
        {
            Size size = _imageSaver.GetFullResolutionSize(CreateFullResolutionImageArgs());
            // Until something has rendered there is no area to measure
            bool measured = size.Width > 0 && size.Height > 0;
            FullResolutionButton.Content = measured ? $"Full resolution  ({size.Width}x{size.Height})" : "Full resolution";
        }

        private void ScreenshotRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            SetScreenshotDescritpion();
            SetFullResolutionPanelEnabled(false);
        }
        private void FullResolutionButton_Checked(object sender, RoutedEventArgs e)
        {
            SetFullScreenResolutionDescritpion();
            SetFullResolutionPanelEnabled(true);
        }
        private void WebMapButton_Checked(object sender, RoutedEventArgs e)
        {
            SetWebMapDescription();
            SetFullResolutionPanelEnabled(false);
            if (WebMapPanel is not null) WebMapPanel.Visibility = Visibility.Visible;
        }

        private void SetFullResolutionPanelEnabled(bool enabled)
        {
            if (FullResolutionPanel is null) return;
            FullResolutionPanel.Visibility = enabled ? Visibility.Visible : Visibility.Hidden;
            if (WebMapPanel is not null) WebMapPanel.Visibility = Visibility.Hidden;
        }
        private void ClipAreaCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckerPatternRadioBox is not null && BlackColorRadioBox is not null) 
            {
                SetFullResolutionLabel();
            }
        }
        private void ClipAreaCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (CheckerPatternRadioBox is not null && BlackColorRadioBox is not null)
            {
                SetFullResolutionLabel();
            }
        }

        private void SetScreenshotDescritpion()
        {
            string description = "Captures the screen. Some image scaling artifacts may be present.";

            if (DescriptionLabel is null) return;
            DescriptionLabel.Text = $"Description: {description}";
        }
        private void SetFullScreenResolutionDescritpion()
        {
            string description = "Saves the image as one pixel per block. Gridlines will note be visible. Image size can be rather large.";

            if (DescriptionLabel is null) return;
            DescriptionLabel.Text = $"Description: {description}";
        }
        private void SetWebMapDescription()
        {
            string description = "Renders the whole dimension, not only what has loaded, into a folder with a page that pans and zooms in any browser. Large worlds take a while.";

            if (DescriptionLabel is null) return;
            DescriptionLabel.Text = $"Description: {description}";
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webMapCancellation is not null)
            {
                _webMapCancellation.Cancel();
                return;
            }
            if (_isSaving) return;

            if (WebMapButton.IsChecked == true)
            {
                await SaveWebMap();
                return;
            }

            Microsoft.Win32.SaveFileDialog saveDialog = new()
            {
                FileName = "RenderedMap",
                DefaultExt = ".png",
                Filter = "Image files (.png)|*.png"
            };

            bool? result = saveDialog.ShowDialog();
            if (result == null || !result.Value) return;

            string path = saveDialog.FileName;
            bool saveScreenshot = ScreenshotRadioButton.IsChecked is not null && ScreenshotRadioButton.IsChecked.Value;
            FullResolutionImageArgs fullResolutionImageArgs = CreateFullResolutionImageArgs();
            Exception? saveException = null;

            BeginSaving();

            try
            {
                await Dispatcher.Yield(DispatcherPriority.Background);

                if (saveScreenshot)
                {
                    SaveScreenshot(path);
                }
                else
                {
                    await RunOnStaThreadAsync(() => SaveFullResolution(path, fullResolutionImageArgs));
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
                MessageBox.Show(this, $"Image could not be saved.\n\n{saveException.Message}", "Save as image", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Close();
        }

        private async Task SaveWebMap()
        {
            Microsoft.Win32.OpenFolderDialog folderDialog = new()
            {
                Title = "Choose an empty folder for the web map"
            };
            if (folderDialog.ShowDialog(this) != true) return;
            string folder = folderDialog.FolderName;

            Action<IProgress<WebMapProgress>?, CancellationToken> export;
            try
            {
                export = _imageSaver.PrepareWebMap(folder, new WebMapOptions() { IncludeMarkers = IncludeMarkersCheckBox.IsChecked == true });
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"Web map could not be exported.\n\n{exception.Message}", "Export map", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Progress<WebMapProgress> progress = new(ShowWebMapProgress);
            using CancellationTokenSource cancellation = new();
            _webMapCancellation = cancellation;
            Exception? saveException = null;

            BeginSaving();
            SavingLabel.Text = "Starting";
            SaveButton.IsEnabled = true;
            SaveButton.Content = "Cancel";

            try
            {
                await Task.Run(() => export(progress, cancellation.Token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                saveException = exception;
            }
            finally
            {
                _webMapCancellation = null;
                SaveButton.Content = "Save as...";
                EndSaving();
            }

            if (cancellation.IsCancellationRequested) return;
            if (saveException is not null)
            {
                MessageBox.Show(this, $"Web map could not be exported.\n\n{saveException.Message}", "Export map", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (OpenWhenDoneCheckBox.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(folder, "index.html")) { UseShellExecute = true });
            }
            Close();
        }

        private void ShowWebMapProgress(WebMapProgress progress)
        {
            SavingLabel.Text = progress.Stage switch
            {
                WebMapStage.Rendering => $"Rendering regions {progress.Done} / {progress.Total}",
                WebMapStage.Zooming => $"Making zoom levels {progress.Done} / {progress.Total}",
                _ => "Finishing"
            };
        }

        private void BeginSaving()
        {
            _isSaving = true;
            _savingDotCount = 0;
            SavingLabel.Text = "Saving";
            SavingLabel.Visibility = Visibility.Visible;
            SetControlsEnabled(false);
            _savingAnimationTimer.Start();
        }

        private void EndSaving()
        {
            _savingAnimationTimer.Stop();
            SavingLabel.Visibility = Visibility.Collapsed;
            SetControlsEnabled(true);
            WebMapButton.IsEnabled = _imageSaver.CanSaveWebMap;
            _isSaving = false;
        }

        private void SavingAnimationTimer_Tick(object? sender, EventArgs e)
        {
            // A web map reports its own progress instead
            if (_webMapCancellation is not null) return;

            _savingDotCount = (_savingDotCount + 1) % 4;
            SavingLabel.Text = $"Saving{new string('.', _savingDotCount)}";
        }

        private void SetControlsEnabled(bool enabled)
        {
            ScreenshotRadioButton.IsEnabled = enabled;
            FullResolutionButton.IsEnabled = enabled;
            WebMapButton.IsEnabled = enabled;
            IncludeMarkersCheckBox.IsEnabled = enabled;
            OpenWhenDoneCheckBox.IsEnabled = enabled;
            ClipAreaCheckBox.IsEnabled = enabled;
            CheckerPatternRadioBox.IsEnabled = enabled;
            BlackColorRadioBox.IsEnabled = enabled;
            InvisibleColorRadioBox.IsEnabled = enabled;
            SaveButton.IsEnabled = enabled;
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            TaskCompletionSource<object?> taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Thread thread = new(() =>
            {
                try
                {
                    action();
                    taskCompletionSource.SetResult(null);
                }
                catch (Exception exception)
                {
                    taskCompletionSource.SetException(exception);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return taskCompletionSource.Task;
        }

        private void SaveScreenshot(string path)
        {
            _imageSaver.SaveAsScreenshot(path);
        }
        private void SaveFullResolution(string path, FullResolutionImageArgs args)
        {
            _imageSaver.SaveAsFullResolution(path, args);
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
