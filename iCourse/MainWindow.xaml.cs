using Newtonsoft.Json.Linq;
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
        static bool isLogged = false;

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
            if (isLogged)
            {
                WriteLine("请勿重复登录！");
                return;
            }
            var name = username.Text;
            var pw = password.Text;
            Web web = new Web(name, pw);

            var (code, msg, response) = web.Login();

            if (code == 200)
            {
                WriteLine(msg);
                isLogged = true;

                var json = JObject.Parse(response);
                MessageBox.Show(response);
                var studentName = json["data"]["student"]["XM"].ToString();
                var studentID = json["data"]["student"]["XH"].ToString();
                var collage = json["data"]["student"]["YXMC"].ToString();

                WriteLine($"姓名：{studentName}");
                WriteLine($"学号：{studentID}");
                WriteLine($"学院：{collage}");
            }
            else
            {
                WriteLine(msg);
                WriteLine("登录失败，请检查用户名和密码是否正确。");
                isLogged = false;
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