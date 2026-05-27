using iCourse.Services;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace iCourse.Helpers;

public sealed class Logger(IUiDispatcher dispatcher, IAppPaths paths) : IDisposable
{
    public ObservableCollection<string> LogMessages { get; } = new();

    private const int MaxLogEntries = 1145;

    public void Initialize()
    {
        var logPath = Path.Combine(paths.LogDirectory, "log.txt");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Minute)
            .CreateLogger();
    }

    public void WriteLine<T>(T msg)
    {
        var logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{msg?.ToString()}";
        Log.Information(logMsg);

        dispatcher.Post(() =>
        {
            LogMessages.Add(logMsg);

            if (LogMessages.Count > MaxLogEntries)
            {
                LogMessages.RemoveAt(0);
            }
        });
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
    }
}
