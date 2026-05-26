namespace iCourse.Services;

public interface IAppPaths
{
    string DataDirectory { get; }
    string LogDirectory { get; }
    string CredentialsFilePath { get; }
    string NoShowDisclaimerFilePath { get; }
}
