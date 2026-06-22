using iCourse.Services;
using Serilog;
using System;
using System.IO;

namespace iCourse.Helpers;

public sealed class Logger(IAppPaths paths) : IDisposable
{
    private Serilog.Core.Logger? fileLogger;

    public void Initialize()
    {
        var logPath = Path.Combine(paths.LogDirectory, "log.txt");
        fileLogger?.Dispose();
        fileLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Minute)
            .CreateLogger();
    }

    public void WriteLine<T>(T msg)
    {
        var logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{msg?.ToString()}";
        fileLogger?.Information(logMsg);
    }

    public void Dispose()
    {
        fileLogger?.Dispose();
        fileLogger = null;
    }
}
