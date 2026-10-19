using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Mapper.Gui
{
    // Writes how far into the process each step of startup happened, and when each assembly loaded, to startup.log
    // beside the settings, replaced every launch. It's how a slow start gets traced on a machine we can't run on.
    // The file is flushed line by line, so it's complete up to the step that hangs even if the app is killed
    public static class StartupLog
    {
        public static string File => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultFile)!, "startup.log");

        private static readonly object _lock = new();
        private static StreamWriter? _writer;
        private static DateTime _processStart;

        public static void Begin()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                _writer = new StreamWriter(File, false) { AutoFlush = true };

                using Process process = Process.GetCurrentProcess();
                _processStart = process.StartTime;
                _writer.WriteLine($"Mapee {Assembly.GetEntryAssembly()?.GetName().Version} started {_processStart:yyyy-MM-dd HH:mm:ss.fff}");
                _writer.WriteLine($"{Environment.OSVersion}, {Environment.ProcessorCount} processors, {AppContext.BaseDirectory}");
                _writer.WriteLine("Milliseconds since the process started:");

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) Write($"  already loaded {assembly.GetName().Name}");
                AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
                Mark("Managed code running");
            }
            catch (Exception)
            {
                _writer = null;
            }
        }

        public static void Mark(string step) => Write(step);

        // Once the world browser is up; assembly loads after that are ordinary use
        public static void End()
        {
            lock (_lock)
            {
                if (_writer is null) return;
                AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            }
            Mark("Startup done");
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private static void AssemblyLoaded(object? sender, AssemblyLoadEventArgs e) => Write($"  loaded {e.LoadedAssembly.GetName().Name}");

        private static void Write(string text)
        {
            lock (_lock)
            {
                if (_writer is null) return;
                try
                {
                    _writer.WriteLine($"{(DateTime.Now - _processStart).TotalMilliseconds,8:F0}  [{Environment.CurrentManagedThreadId,2}] {text}");
                }
                catch (Exception)
                {
                    _writer = null;
                }
            }
        }
    }
}
