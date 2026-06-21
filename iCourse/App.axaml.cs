using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using iCourse.ViewModels;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace iCourse;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = DesignTimeServices.Create();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        ServiceProvider = serviceCollection.BuildServiceProvider();
        ServiceProvider.GetRequiredService<Logger>().Initialize();

        AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

        var credentials = ServiceProvider.GetRequiredService<UserCredentials>();
        credentials.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => ShutdownServices();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IAppLifetime, AppLifetime>();
        services.AddSingleton<IImageDecoder, AvaloniaImageDecoder>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<Logger>();
        services.AddSingleton<UserCredentials>();
        services.AddSingleton(new CourseSelectionOptions());
        services.AddSingleton<CourseSelectionResponseClassifier>();
        services.AddSingleton<ICourseSelectionDelay, AggressiveCourseSelectionDelay>();
        services.AddSingleton<CourseSelectionEngine>();
        services.AddSingleton<JLUiCourseApi>();
        services.AddSingleton<IJLUiCourseApi>(provider => provider.GetRequiredService<JLUiCourseApi>());
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<QueryCourseWindowViewModel>();
        services.AddTransient<MainWindow>();
    }

    private static void ShutdownServices()
    {
        AppDomain.CurrentDomain.UnhandledException -= UnhandledExceptionHandler;
        ServiceProvider.GetService<Logger>()?.Dispose();
    }

    private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    private static void LogException(Exception exception)
    {
        var paths = ServiceProvider.GetRequiredService<IAppPaths>();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var logFilePath = Path.Combine(paths.LogDirectory, $"error_{timestamp}.log");
        File.AppendAllText(logFilePath, $"[{DateTime.Now}] {exception}\n");
    }
}
