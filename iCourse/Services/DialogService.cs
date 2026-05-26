using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using iCourse.Models;
using iCourse.ViewModels;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public Task ShowMessageAsync(string title, string message)
    {
        return RunOnUiThreadAsync(async () =>
        {
            var window = new MessageWindow
            {
                DataContext = ActivatorUtilities.CreateInstance<MessageWindowViewModel>(services, title, message)
            };

            await window.ShowDialog(GetOwnerOrThrow());
        });
    }

    public Task<string?> ShowCaptchaAsync(string base64Image)
    {
        return RunOnUiThreadAsync(async () =>
        {
            var viewModel = ActivatorUtilities.CreateInstance<CaptchaWindowViewModel>(services, base64Image);
            var window = new CaptchaWindow { DataContext = viewModel };
            return await window.ShowDialog<string?>(GetOwnerOrThrow());
        });
    }

    public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches)
    {
        return RunOnUiThreadAsync(async () =>
        {
            var viewModel = ActivatorUtilities.CreateInstance<SelectBatchViewModel>(services, batches);
            var window = new SelectBatchWindow { DataContext = viewModel };
            return await window.ShowDialog<BatchInfo?>(GetOwnerOrThrow());
        });
    }

    public Task ShowQueryCoursesAsync()
    {
        return RunOnUiThreadAsync(async () =>
        {
            var viewModel = ActivatorUtilities.CreateInstance<QueryCourseWindowViewModel>(services);
            var window = new QueryCourseWindow { DataContext = viewModel };
            await window.ShowDialog(GetOwnerOrThrow());
        });
    }

    private static Window GetOwnerOrThrow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is not null)
        {
            return desktop.MainWindow;
        }

        throw new InvalidOperationException("No main window is available for modal dialogs.");
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        var taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action();
                taskCompletionSource.SetResult();
            }
            catch (Exception exception)
            {
                taskCompletionSource.SetException(exception);
            }
        });

        return taskCompletionSource.Task;
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        var taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var result = await action();
                taskCompletionSource.SetResult(result);
            }
            catch (Exception exception)
            {
                taskCompletionSource.SetException(exception);
            }
        });

        return taskCompletionSource.Task;
    }
}
