using iCourse.Helpers;
using iCourse.Tests.Fakes;
using System.Text.RegularExpressions;

namespace iCourse.Tests.Helpers;

public sealed class LoggerTests
{
    [Fact]
    public void WriteLine_WritesTimestampedMessageToLogFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        var paths = new FakeAppPaths(root);

        using var logger = new Logger(paths);
        logger.Initialize();
        logger.WriteLine("hello");
        logger.Dispose();

        var logFile = Assert.Single(Directory.GetFiles(paths.LogDirectory, "log*.txt"));
        var contents = File.ReadAllText(logFile);
        Assert.Contains("hello", contents);
        Assert.Matches(
            new Regex(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]hello"),
            contents);
    }

    [Fact]
    public void MultipleLoggers_WriteToTheirOwnFilesIndependently()
    {
        var firstPaths = CreateTempPaths();
        var secondPaths = CreateTempPaths();
        using var first = new Logger(firstPaths);
        using var second = new Logger(secondPaths);
        first.Initialize();
        second.Initialize();

        first.WriteLine("first");
        second.WriteLine("second");
        first.Dispose();
        second.Dispose();

        Assert.Contains(
            "first",
            File.ReadAllText(Assert.Single(
                Directory.GetFiles(firstPaths.LogDirectory, "log*.txt"))));
        Assert.Contains(
            "second",
            File.ReadAllText(Assert.Single(
                Directory.GetFiles(secondPaths.LogDirectory, "log*.txt"))));
    }

    private static FakeAppPaths CreateTempPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));
        return new FakeAppPaths(root);
    }
}
