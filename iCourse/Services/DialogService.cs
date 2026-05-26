using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
    private Window? Owner
    {
        get
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }

            return null;
        }
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = new MessageWindow
        {
            DataContext = ActivatorUtilities.CreateInstance<MessageWindowViewModel>(services, title, message)
        };

        if (Owner is null)
        {
            window.Show();
            return;
        }

        await window.ShowDialog(Owner);
    }

    public async Task<string?> ShowCaptchaAsync(string base64Image)
    {
        var viewModel = ActivatorUtilities.CreateInstance<CaptchaWindowViewModel>(services, base64Image);
        var window = new CaptchaWindow { DataContext = viewModel };
        return Owner is null ? null : await window.ShowDialog<string?>(Owner);
    }

    public async Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches)
    {
        var viewModel = ActivatorUtilities.CreateInstance<SelectBatchViewModel>(services, batches);
        var window = new SelectBatchWindow { DataContext = viewModel };
        return Owner is null ? null : await window.ShowDialog<BatchInfo?>(Owner);
    }

    public async Task ShowQueryCoursesAsync()
    {
        var viewModel = ActivatorUtilities.CreateInstance<QueryCourseWindowViewModel>(services);
        var window = new QueryCourseWindow { DataContext = viewModel };
        if (Owner is not null)
        {
            await window.ShowDialog(Owner);
        }
    }
}
