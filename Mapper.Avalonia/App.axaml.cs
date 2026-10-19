using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Mapper.Gui
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            StartupLog.Mark("Avalonia initialised");
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptions;
            InitializeBackgroundWork();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // A world can be named on the command line, which is also how file managers open a .mcworld with Mapee
                string? world = desktop.Args?.FirstOrDefault(arg => !arg.StartsWith('-'));
                Controller.Controller controller = new Controller.Controller(startupWorld: world);
                desktop.MainWindow = controller.MainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        // The app folder may be read-only (an AppImage is), so crash reports go to the user's data folder
        public static string CrashDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mapee", "Crashes");

        private static void UnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
        {
            Directory.CreateDirectory(CrashDirectory);
            string fileName = Path.Combine(CrashDirectory, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.txt");

            StringBuilder text = new StringBuilder();
            if (e.ExceptionObject is Exception exception)
            {
                text.AppendLine(exception.Message);
                text.Append(exception.StackTrace);
            }

            File.WriteAllText(fileName, text.ToString());
        }

        private static void InitializeBackgroundWork()
        {
            BackgroundWork.Run(TimeSpan.FromSeconds(5), () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }
    }
}
