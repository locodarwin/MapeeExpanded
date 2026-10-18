using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [STAThread]
        protected override void OnStartup(StartupEventArgs e)
        {
            // A world can be named on the command line, which is also how Explorer opens a .mcworld with Mapee or a
            // world dropped on Mapee.exe. Explorer may start Mapee in that file's folder, so the path is made whole
            // before moving to the app's folder, where Resources are
            string? world = e.Args.FirstOrDefault(arg => !arg.StartsWith('-'));
            if (world is not null) world = Path.GetFullPath(world);
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptions;
            InitializeBackgroundWork();

            Controller.Controller controller = new Controller.Controller(startupWorld: world);
            controller.MainWindow.ShowDialog();
        }

        private static void UnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
        {
            Directory.CreateDirectory("Crashes");
            string fileName = $"Crashes\\{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.txt";

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
