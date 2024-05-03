using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
