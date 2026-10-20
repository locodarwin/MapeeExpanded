using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

        // Set while a web map exports; the save button cancels it
        private CancellationTokenSource? _webMapCancellation;
        private string _webMapStatus = string.Empty;

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
                ShowPanel(null);
            };
            FullResolutionButton.IsCheckedChanged += (sender, e) =>
            {
                if (FullResolutionButton.IsChecked != true) return;
                SetFullScreenResolutionDescritpion();
                ShowPanel(FullResolutionPanel);
            };
            WebMapButton.IsCheckedChanged += (sender, e) =>
            {
                if (WebMapButton.IsChecked != true) return;
                SetWebMapDescription();
                ShowPanel(WebMapPanel);
            };
            ClipAreaCheckBox.IsCheckedChanged += (sender, e) => SetFullResolutionLabel();

            SetScreenshotDescritpion();
            ShowPanel(null);

            SetScreenshotLabel();
            SetFullResolutionLabel();
            WebMapButton.IsEnabled = _imageSaver.CanSaveWebMap;
            IncludeMarkupCheckBox.IsVisible = _imageSaver.HasMarkup;
            WebMapDescriptionLabel.Text = _imageSaver.WebMapDescription;

            Closing += (sender, e) => _webMapCancellation?.Cancel();
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

        private void ShowPanel(Control? panel)
        {
            foreach (Control each in new Control[] { FullResolutionPanel, WebMapPanel })
            {
                each.Opacity = each == panel ? 1 : 0;
                each.IsHitTestVisible = each == panel;
            }
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
        private void SetWebMapDescription()
        {
            string description = "Renders the Overworld, Nether and End, whichever the world has and all of each, into a folder with a page that pans, zooms and switches between them in any browser. Large worlds take a while.";
            DescriptionLabel.Text = $"Description: {description}";
        }

        private async void SaveButton_Click(object? sender, RoutedEventArgs e)
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
                    _imageSaver.SaveAsScreenshot(path, IncludeMarkupCheckBox.IsChecked == true);
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

        private async Task SaveWebMap()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Choose a folder for the web map",
                AllowMultiple = false
            });
            string? folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (folder is null) return;

            if (ReplaceWarning(_imageSaver.WebMapWillReplace(folder)) is string warning &&
                !await MessageWindow.Confirm(warning, "Export map", "Replace", "Cancel", this)) return;

            Action<IProgress<WebMapProgress>?, CancellationToken> export;
            try
            {
                export = _imageSaver.PrepareWebMap(folder, new WebMapOptions() { IncludeMarkers = IncludeMarkersCheckBox.IsChecked == true, IncludeMarkup = IncludeMarkupCheckBox.IsChecked == true });
            }
            catch (Exception exception)
            {
                await MessageWindow.Show($"Web map could not be exported.\n\n{exception.Message}", "Export map", this);
                return;
            }

            Progress<WebMapProgress> progress = new(ShowWebMapProgress);
            using CancellationTokenSource cancellation = new();
            _webMapCancellation = cancellation;
            _webMapStatus = "Starting";
            Exception? saveException = null;

            BeginSaving();
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
                await MessageWindow.Show($"Web map could not be exported.\n\n{saveException.Message}", "Export map", this);
                return;
            }

            if (OpenWhenDoneCheckBox.IsChecked == true)
            {
                await Launcher.LaunchFileInfoAsync(new FileInfo(Path.Combine(folder, "index.html")));
            }
            Close();
        }

        // Nothing is asked when the folder is empty or the export only adds to it
        private static string? ReplaceWarning(IList<string> replaced)
        {
            if (replaced.Count == 0) return null;
            return $"This folder already holds web map files: {string.Join(", ", replaced)}.\n\n" +
                "Exporting here replaces them, along with any web map exported here before. Anything else in the folder is left alone.";
        }

        private void ShowWebMapProgress(WebMapProgress progress)
        {
            string dimension = progress.Dimension is null ? string.Empty : $"{progress.Dimension}: ";
            _webMapStatus = progress.Stage switch
            {
                WebMapStage.Rendering => $"{dimension}rendering regions {progress.Done} / {progress.Total}",
                WebMapStage.Zooming => $"{dimension}making zoom levels {progress.Done} / {progress.Total}",
                _ => "Finishing"
            };
            SavingLabel.Text = _webMapStatus;
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
            WebMapButton.IsEnabled = _imageSaver.CanSaveWebMap;
            IncludeMarkupCheckBox.IsVisible = _imageSaver.HasMarkup;
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
            ClipAreaCheckBox.IsEnabled = enabled;
            CheckerPatternRadioBox.IsEnabled = enabled;
            BlackColorRadioBox.IsEnabled = enabled;
            InvisibleColorRadioBox.IsEnabled = enabled;
            IncludeMarkersCheckBox.IsEnabled = enabled;
            IncludeMarkupCheckBox.IsEnabled = enabled;
            OpenWhenDoneCheckBox.IsEnabled = enabled;
            SaveButton.IsEnabled = enabled;
        }

        private FullResolutionImageArgs CreateFullResolutionImageArgs()
        {
            return new FullResolutionImageArgs()
            {
                ClipArea = ClipAreaCheckBox.IsChecked is not null && ClipAreaCheckBox.IsChecked.Value,
                CheckerPatternEnabled = CheckerPatternRadioBox.IsChecked is not null && CheckerPatternRadioBox.IsChecked.Value,
                BackgroundColor = BlackColorRadioBox.IsChecked is not null && BlackColorRadioBox.IsChecked.Value ? Colors.Black : Color.FromArgb(0, 0, 0, 0),
                IncludeMarkup = IncludeMarkupCheckBox.IsChecked == true
            };
        }
    }
}
