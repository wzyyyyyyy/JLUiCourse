using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.ViewModels;
using System.Windows;

namespace iCourse.Views
{
    /// <summary>
    /// CaptchaWindow.xaml 的交互逻辑
    /// </summary>
    partial class CaptchaWindow : Window
    {
        public CaptchaWindow(string base64Image)
        {
            InitializeComponent();
            DataContext = new CaptchaWindowViewModel(base64Image);
            WeakReferenceMessenger.Default.Register<CloseWindowMessage>(this, CloseWindow);
        }

        private void CloseWindow(object recipient, CloseWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(CaptchaWindowViewModel))
            {
                this.Close();
            }
        }
    }
}
