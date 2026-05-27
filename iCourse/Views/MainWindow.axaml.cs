using Avalonia.Controls;
using iCourse.Services;
using iCourse.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;

namespace iCourse.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await ShowDisclaimerWindowAsync();
    }

    private async Task ShowDisclaimerWindowAsync()
    {
        var paths = App.ServiceProvider.GetRequiredService<IAppPaths>();
        if (File.Exists(paths.NoShowDisclaimerFilePath))
        {
            return;
        }

        var viewModel = ActivatorUtilities.CreateInstance<DisclaimerViewModel>(App.ServiceProvider);
        var window = new DisclaimerWindow { DataContext = viewModel };
        var agreed = await window.ShowDialog<bool>(this);
        if (!agreed)
        {
            App.ServiceProvider.GetRequiredService<IAppLifetime>().Shutdown();
        }
    }
}
