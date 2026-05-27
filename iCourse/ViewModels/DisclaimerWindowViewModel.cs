using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Services;
using System.IO;

namespace iCourse.ViewModels;

public partial class DisclaimerViewModel(IAppPaths paths, IAppLifetime lifetime) : ObservableObject
{
    [ObservableProperty]
    private bool isAgreed;

    [ObservableProperty]
    private bool noShowNextTime;

    [RelayCommand]
    private void Agree(Window window)
    {
        if (NoShowNextTime)
        {
            File.WriteAllText(paths.NoShowDisclaimerFilePath, "1");
        }

        window.Close(true);
    }

    [RelayCommand]
    private void Decline()
    {
        lifetime.Shutdown();
    }
}
