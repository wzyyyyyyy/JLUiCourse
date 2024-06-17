using System.Windows;
using System.Windows.Controls;

namespace iCourse
{
    public partial class MainWindow : Window
    {
        static bool isLogged = false;

        public static MainWindow Instance { get; private set; }

        public static Web web { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }

        private async Task LoginAsync()
        {
            if (isLogged)
            {
                WriteLine("请勿重复登录！");
                return;
            }
            var name = username.Text;
            var pw = password.Password;
            web = new Web(name, pw);

            // 登录
            var (code, msg) = await web.LoginAsync();
            var response = web.GetLoginResponse();

            // 登录失败
            if (code != 200)
            {
                WriteLine(msg);
                WriteLine("登录失败，请检查用户名和密码是否正确。");
                isLogged = false;
                return;
            }

            // 登录成功
            WriteLine(msg);
            isLogged = true;

            // 获取学生信息
            var studentName = response["data"]["student"]["XM"].ToString();
            var studentID = response["data"]["student"]["XH"].ToString();
            var collage = response["data"]["student"]["YXMC"].ToString();

            WriteLine($"姓名：{studentName}");
            WriteLine($"学号：{studentID}");
            WriteLine($"学院：{collage}");

            // 显示选课批次
            var batchInfos = web.GetBatchInfo();
            SelectBatchWindow selectBatchWindow = new SelectBatchWindow(batchInfos);
            selectBatchWindow.ShowDialog();
        }

        public async void StartSelectClass(BatchInfo batch)
        {
            web.SetBatchID(batch);
            var list = await web.GetFavoriteCourses(batch);
            foreach (var course in list)
            {
                Task.Run(() =>
                {
                    web.SelectCourse(batch, course);
                });
            }
        }

        public void WriteLine(string msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                ConsoleBox.Text += "[" + time + "] : " + msg + "\n";
                ConsoleScrollViewer.ScrollToEnd();
            });
        }
    }
}
