using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using System.Windows.Media.Imaging;
using System.IO;

namespace iCourse.ViewModels
{
    partial class CaptchaWindowViewModel : ObservableObject
    {
        [ObservableProperty] 
        private string captcha;

        [ObservableProperty]
        private BitmapImage imageSource;

        public ICommand CloseCommand { get; }

        public CaptchaWindowViewModel()
        {
            CloseCommand = new RelayCommand(CloseWindow);
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

        private void CloseWindow()
        {
            WeakReferenceMessenger.Default.Send<CloseWindowMessage>();
        }
    }
}
