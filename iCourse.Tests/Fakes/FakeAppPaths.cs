using iCourse.Services;

namespace iCourse.Tests.Fakes;

internal sealed class FakeAppPaths(string root) : IAppPaths
{
    public string DataDirectory { get; } = root;
    public string LogDirectory { get; } = Path.Combine(root, "Logs");
    public string CredentialsFilePath { get; } = Path.Combine(root, "credentials.json");
    public string NoShowDisclaimerFilePath { get; } = Path.Combine(root, ".noshow");
}
