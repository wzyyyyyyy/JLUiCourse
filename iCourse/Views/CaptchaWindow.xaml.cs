using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;

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
            WeakReferenceMessenger.Default.Register<CloseWindowMessage>(this, CloseWindow);
        }

        private void CloseWindow(object recipient,CloseWindowMessage msg)
        {
            this.Close();
        }
    }
}
