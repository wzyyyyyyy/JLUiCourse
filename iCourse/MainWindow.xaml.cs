using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace iCourse
{
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            Login();
        }

        private void Login()
        {
            var name = username.Text;
            var pw = password.Text;
            Web web = new Web(name, pw);

            var (code, msg) = web.Login();

            if (code == 200)
            {
                WriteLine(msg);
            }
            else
            {
                WriteLine(msg);
                WriteLine("登录失败，请检查用户名和密码是否正确。");
            }

        }

        private void WriteLine(string msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            ConsoleBox.Text += "["+time+"] : "+msg + "\n";
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}