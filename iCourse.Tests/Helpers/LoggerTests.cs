using iCourse.Helpers;
using iCourse.Tests.Fakes;

namespace iCourse.Tests.Helpers;

public sealed class LoggerTests
{
    [Fact]
    public void WriteLine_AddsTimestampedMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));

        var logger = new Logger(new ImmediateUiDispatcher(), new FakeAppPaths(root));
        logger.Initialize();

        logger.WriteLine("hello");

        Assert.Single(logger.LogMessages);
        Assert.Contains("hello", logger.LogMessages[0]);
        Assert.StartsWith("[", logger.LogMessages[0]);
    }
}
