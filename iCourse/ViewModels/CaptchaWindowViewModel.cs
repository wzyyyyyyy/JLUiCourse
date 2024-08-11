using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using System.IO;
using System.Windows.Media.Imaging;
using iCourse.Messages;

namespace iCourse.ViewModels
{
    partial class CaptchaWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string captcha;

        [ObservableProperty]
        private BitmapImage imageSource;

        public CaptchaWindowViewModel()
        {

        }

        public CaptchaWindowViewModel(string base64Image)
        {
            LoadCaptchaImage(base64Image);
        }

        private void LoadCaptchaImage(string base64Image)
        {
            var imageBytes = Convert.FromBase64String(base64Image);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(imageBytes);
            bitmap.EndInit();

            ImageSource = bitmap;
        }

        [RelayCommand]
        private void CloseWindow()
        {
            WeakReferenceMessenger.Default.Send<AttemptLoginMessage>(new AttemptLoginMessage(Captcha));
            WeakReferenceMessenger.Default.Send<CloseWindowMessage>(new CloseWindowMessage(typeof(CaptchaWindowViewModel)));
        }
    }
}
