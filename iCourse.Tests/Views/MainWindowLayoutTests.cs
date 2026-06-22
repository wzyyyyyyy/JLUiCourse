using System.Xml.Linq;

namespace iCourse.Tests.Views;

public sealed class MainWindowLayoutTests
{
    private static readonly string MainWindowPath = FindRepoFile(
        "iCourse",
        "Views",
        "MainWindow.axaml");

    private static readonly string LoginControlPath = FindRepoFile(
        "iCourse",
        "Views",
        "LoginControl.axaml");

    [Fact]
    public void MainWindow_UsesCourseStatusWorkspaceInsteadOfUiLog()
    {
        var markup = File.ReadAllText(MainWindowPath);

        Assert.Contains("CourseStatuses", markup);
        Assert.Contains("BannerText", markup);
        Assert.Contains("SucceededCount", markup);
        Assert.Contains("RunningCount", markup);
        Assert.DoesNotContain("LogMessages", markup);
        Assert.DoesNotContain("AutoScroll", markup);
        Assert.DoesNotContain("iCourse.Behaviors", markup);
    }

    [Fact]
    public void LoginControl_ExposesStopSelectionCommand()
    {
        var markup = File.ReadAllText(LoginControlPath);

        Assert.Contains("StopSelectCourseCommand", markup);
        Assert.Contains("IsSelectionRunning", markup);
        Assert.Contains("CanStartSelection", markup);
    }

    [Fact]
    public void MainWindowMarkup_IsWellFormedXml()
    {
        _ = XDocument.Load(MainWindowPath);
        _ = XDocument.Load(LoginControlPath);
    }

    private static string FindRepoFile(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {Path.Combine(relativePath)} from {AppContext.BaseDirectory}.");
    }
}
