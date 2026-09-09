using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace iCourse.Tests;

[CollectionDefinition("Headless UI", DisableParallelization = true)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessFixture>;

public sealed class HeadlessFixture : IDisposable
{
    private readonly HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public Task Run(Action action) => session.Dispatch(action, CancellationToken.None);

    public Task Run(Func<Task> action) => session.Dispatch(async () =>
    {
        await action();
        return true;
    }, CancellationToken.None);

    public void Dispose() => session.Dispose();
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class TestApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
