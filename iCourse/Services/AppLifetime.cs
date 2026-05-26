using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Diagnostics;

namespace iCourse.Services;

public sealed class AppLifetime : IAppLifetime
{
    public void Shutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Restart()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }

        Shutdown();
    }
}
