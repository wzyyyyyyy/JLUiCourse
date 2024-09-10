using Serilog;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace iCourse.Helpers
{
    public class Logger
    {
        public ObservableCollection<string> LogMessages { get; } = new();

        private const int MaxLogEntries = 1145;

        public Logger()
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "log.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath
                , rollingInterval: RollingInterval.Minute)
                .CreateLogger();
        }

        public void WriteLine<T>(T msg)
        {
            var logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{msg?.ToString()}";
            Log.Information(logMsg);

            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(logMsg);

                if (LogMessages.Count > MaxLogEntries)
                {
                    //Remove the oldest entry
                    LogMessages.RemoveAt(0);
                }
            });
        }

        public void Dispose() => Log.CloseAndFlush();
    }
}