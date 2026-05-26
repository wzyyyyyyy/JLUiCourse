using System;
using System.IO;

namespace iCourse.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DataDirectory = Path.Combine(appData, "JLUiCourse");
        LogDirectory = Path.Combine(DataDirectory, "Logs");
        CredentialsFilePath = Path.Combine(DataDirectory, "credentials.json");
        NoShowDisclaimerFilePath = Path.Combine(DataDirectory, ".noshow");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string CredentialsFilePath { get; }
    public string NoShowDisclaimerFilePath { get; }
}
