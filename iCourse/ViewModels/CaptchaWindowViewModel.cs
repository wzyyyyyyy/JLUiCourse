using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Services;

namespace iCourse.ViewModels;

public partial class CaptchaWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string captcha = string.Empty;

    [ObservableProperty]
    private Bitmap imageSource;

    public CaptchaWindowViewModel(IImageDecoder imageDecoder, string base64Image)
    {
        ImageSource = imageDecoder.DecodeBase64Bitmap(base64Image);
    }

    [RelayCommand]
    private void CloseWindow(Window window)
    {
        window.Close(Captcha);
    }
}
