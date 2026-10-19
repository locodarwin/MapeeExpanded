using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Mapper.Gui
{
    // Writes how far into the process each step of startup happened, and when each assembly loaded, to startup.log
    // beside the settings, replaced every launch; then the first world's load (regions queued, rendered and painted)
    // until it has been open a while. It's how a slow start or a blank map gets traced on a machine we can't run on.
    // The file is flushed line by line, so it's complete up to the step that hangs even if the app is killed
    public static class StartupLog
    {
        public static string File => Path.Combine(Path.GetDirectoryName(AppSettings.DefaultFile)!, "startup.log");

        private static readonly object _lock = new();
        private static StreamWriter? _writer;
        private static DateTime _processStart;
        private static readonly System.Collections.Generic.Dictionary<string, string> _lastRepeatable = new();

        // False once the log is closed, so callers in busy paths can skip building their text
        public static bool Active => _writer is not null;

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

        // For steps that repeat many times a second: written only when the text differs from the last line of that kind
        public static void MarkChange(string kind, string step)
        {
            lock (_lock)
            {
                if (_lastRepeatable.TryGetValue(kind, out string? last) && last == step) return;
                _lastRepeatable[kind] = step;
            }
            Write(step);
        }

        // Once the world browser is up; assembly loads after that are ordinary use
        public static void StopAssemblyLog()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
            Mark("Startup done");
        }

        // Once the first world has had time to draw
        public static void EndAfter(TimeSpan delay)
        {
            System.Threading.Tasks.Task.Delay(delay).ContinueWith(_ =>
            {
                Mark("Log ends");
                lock (_lock)
                {
                    _writer?.Dispose();
                    _writer = null;
                }
            });
        }

        public static string Describe(Mapper.Gui.Model.XzRange range) =>
            $"{range.TopLeftPoint.X:F0},{range.TopLeftPoint.Z:F0} to {range.BottomRightPoint.X:F0},{range.BottomRightPoint.Z:F0}";

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
