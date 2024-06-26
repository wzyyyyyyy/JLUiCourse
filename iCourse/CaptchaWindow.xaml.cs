using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace iCourse
{
    /// <summary>
    /// CaptchaWindow.xaml 的交互逻辑
    /// </summary>
    public partial class CaptchaWindow : Window
    {
        public static string Captcha { get; private set; }

        public CaptchaWindow(string base64Image)
        {
            InitializeComponent();

            byte[] imageBytes = Convert.FromBase64String(base64Image);

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(imageBytes);
            bitmap.EndInit();

            image.Source = bitmap;
        }


        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Captcha = ((TextBox)sender).Text;
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                this.Close();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
