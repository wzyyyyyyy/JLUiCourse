using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace iCourse.ViewModels;

public partial class MessageWindowViewModel(string title, string message) : ObservableObject
{
    public string Title { get; } = title;
    public string Message { get; } = message;

    [RelayCommand]
    private static void Close(Window window)
    {
        window.Close();
    }
}
