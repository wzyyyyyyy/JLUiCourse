using iCourse.Helpers;
using iCourse.Models;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace iCourse.Services;

public static class DesignTimeServices
{
    public static IServiceProvider Create()
    {
        var services = new ServiceCollection();
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
        return services.BuildServiceProvider();
    }
}
