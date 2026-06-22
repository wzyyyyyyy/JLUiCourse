using System.Globalization;
using System.Xml.Linq;

namespace iCourse.Tests.Views;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void MainWindow_DefinesReadOnlyPerCourseStatusColumns()
    {
        var document = LoadView("MainWindow.axaml");
        var dataGrid = SingleElement(document, "DataGrid");

        Assert.Equal("{Binding CourseStatuses}", Attribute(dataGrid, "ItemsSource"));
        Assert.Equal("False", Attribute(dataGrid, "AutoGenerateColumns"));
        Assert.Equal("True", Attribute(dataGrid, "IsReadOnly"));

        var columns = Assert.Single(
            dataGrid.Elements(),
            element => element.Name.LocalName == "DataGrid.Columns").Elements().ToList();
        Assert.Equal(
            ["课程", "状态", "尝试次数", "耗时", "最新结果"],
            columns.Select(column => Attribute(column, "Header")));

        AssertWrappedTemplateColumn(columns, "课程", "CourseName");
        AssertWrappedTemplateColumn(columns, "最新结果", "LatestResult");
        AssertTextColumnBinding(columns, "尝试次数", "AttemptCount");
        AssertTextColumnBinding(columns, "耗时", "ElapsedText");

        var statusColumn = Column(columns, "状态");
        Assert.Equal("DataGridTemplateColumn", statusColumn.Name.LocalName);
        var statusBorder = Assert.Single(
            statusColumn.Descendants(),
            element => element.Name.LocalName == "Border" &&
                AttributeOrDefault(element, "Classes") == "status-badge");
        Assert.Equal("{Binding IsWaiting}", Attribute(statusBorder, "Classes.waiting"));
        Assert.Equal("{Binding IsRacing}", Attribute(statusBorder, "Classes.racing"));
        Assert.Equal("{Binding IsBackingOff}", Attribute(statusBorder, "Classes.backoff"));
        Assert.Equal("{Binding IsSucceeded}", Attribute(statusBorder, "Classes.success"));
        Assert.Equal("{Binding IsFailed}", Attribute(statusBorder, "Classes.failure"));
        Assert.Equal(
            "{Binding StateText}",
            Attribute(SingleElement(statusColumn, "TextBlock"), "Text"));
    }

    [Fact]
    public void MainWindow_BannerUsesSemanticSeverityClassesAndVisibleText()
    {
        var document = LoadView("MainWindow.axaml");
        var banner = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "Border" &&
                AttributeOrDefault(element, "IsVisible") == "{Binding IsBannerVisible}");

        Assert.Equal("selection-banner", Attribute(banner, "Classes"));
        Assert.Equal("{Binding IsBannerInfo}", Attribute(banner, "Classes.info"));
        Assert.Equal("{Binding IsBannerWarning}", Attribute(banner, "Classes.warning"));
        Assert.Equal("{Binding IsBannerError}", Attribute(banner, "Classes.error"));
        Assert.Equal(
            "{Binding BannerText}",
            Attribute(SingleElement(banner, "TextBlock"), "Text"));

        AssertSeverityStyle(document, "Border.selection-banner.info");
        AssertSeverityStyle(document, "Border.selection-banner.warning");
        AssertSeverityStyle(document, "Border.selection-banner.error");
    }

    [Fact]
    public void MainWindow_UsesCompactSummaryProgressAndSafeMinimumWidth()
    {
        var document = LoadView("MainWindow.axaml");
        var window = Assert.IsType<XElement>(document.Root);

        Assert.True(
            double.Parse(Attribute(window, "MinWidth"), CultureInfo.InvariantCulture) >= 900);
        AssertBindingExists(document, "TotalCount");
        AssertBindingExists(document, "RunningCount");
        AssertBindingExists(document, "SucceededCount");
        AssertBindingExists(document, "FailedCount");

        var progressBar = SingleElement(document, "ProgressBar");
        Assert.Equal("{Binding IsProgressVisible}", Attribute(progressBar, "IsVisible"));
        Assert.Equal("{Binding ProgressValue}", Attribute(progressBar, "Value"));
    }

    [Fact]
    public void LoginControl_ExposesStartStopAndQueryActions()
    {
        var document = LoadView("LoginControl.axaml");
        var buttons = document.Descendants().Where(
            element => element.Name.LocalName == "Button").ToList();

        var start = Button(buttons, "开始选课");
        Assert.Equal("{Binding CanStartSelection}", Attribute(start, "IsEnabled"));
        Assert.Equal("{Binding StartSelectCourseCommand}", Attribute(start, "Command"));

        var stop = Button(buttons, "停止选课");
        Assert.Equal("{Binding IsSelectionRunning}", Attribute(stop, "IsVisible"));
        Assert.Equal("{Binding StopSelectCourseCommand}", Attribute(stop, "Command"));

        Assert.Equal(
            "{Binding QueryCoursesCommand}",
            Attribute(Button(buttons, "查询课程"), "Command"));
    }

    [Fact]
    public void MainWindow_HasNoUiLogOrAutoScrollBehaviorBindings()
    {
        var document = LoadView("MainWindow.axaml");
        var attributes = Assert.IsType<XElement>(document.Root).DescendantsAndSelf().SelectMany(
            element => element.Attributes()).ToList();

        Assert.DoesNotContain(attributes, attribute =>
            attribute.Value == "{Binding LogMessages}");
        Assert.DoesNotContain(attributes, attribute =>
            attribute.Value == "clr-namespace:iCourse.Behaviors");
        Assert.DoesNotContain(attributes, attribute =>
            attribute.Name.LocalName == "AutoScrollToEndBehavior.ItemsSource");
    }

    private static void AssertWrappedTemplateColumn(
        IReadOnlyList<XElement> columns,
        string header,
        string propertyName)
    {
        var column = Column(columns, header);
        Assert.Equal("DataGridTemplateColumn", column.Name.LocalName);
        var textBlock = SingleElement(column, "TextBlock");
        Assert.Equal($"{{Binding {propertyName}}}", Attribute(textBlock, "Text"));
        Assert.Equal("Wrap", Attribute(textBlock, "TextWrapping"));
        Assert.Equal("2", Attribute(textBlock, "MaxLines"));
        Assert.Equal($"{{Binding {propertyName}}}", Attribute(textBlock, "ToolTip.Tip"));
    }

    private static void AssertTextColumnBinding(
        IReadOnlyList<XElement> columns,
        string header,
        string propertyName)
    {
        var column = Column(columns, header);
        Assert.Equal("DataGridTextColumn", column.Name.LocalName);
        Assert.Equal($"{{Binding {propertyName}}}", Attribute(column, "Binding"));
    }

    private static void AssertSeverityStyle(XDocument document, string selector)
    {
        var style = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "Style" &&
                AttributeOrDefault(element, "Selector") == selector);
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            AttributeOrDefault(element, "Property") == "Background");
    }

    private static void AssertBindingExists(XDocument document, string propertyName)
    {
        Assert.Contains(
            document.Descendants().SelectMany(element => element.Attributes()),
            attribute => attribute.Value == $"{{Binding {propertyName}}}");
    }

    private static XElement Button(IReadOnlyList<XElement> buttons, string content) =>
        Assert.Single(buttons, button => AttributeOrDefault(button, "Content") == content);

    private static XElement Column(IReadOnlyList<XElement> columns, string header) =>
        Assert.Single(columns, column => AttributeOrDefault(column, "Header") == header);

    private static XElement SingleElement(XContainer container, string localName) =>
        Assert.Single(container.Descendants(), element => element.Name.LocalName == localName);

    private static string Attribute(XElement element, string localName)
    {
        var attribute = Assert.Single(
            element.Attributes(),
            candidate => candidate.Name.LocalName == localName);
        return attribute.Value;
    }

    private static string? AttributeOrDefault(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(
            candidate => candidate.Name.LocalName == localName)?.Value;

    private static XDocument LoadView(string fileName) =>
        XDocument.Load(Path.Combine(FindRepoRoot(), "iCourse", "Views", fileName));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "iCourse.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate iCourse.sln from {AppContext.BaseDirectory}.");
    }
}
