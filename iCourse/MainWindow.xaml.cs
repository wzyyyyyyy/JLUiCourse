using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace iCourse
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        static bool isLogged = false;

        public static MainWindow Instance { get; private set; }

        public static Web web { get; private set; }

        public static UserCredentials Credentials { get; private set; }

        private ObservableCollection<string> _logMessages;
        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set
            {
                _logMessages = value;
                OnPropertyChanged(nameof(LogMessages));
            }
        }

        public MainWindow()
        {
            ShowDisclaimer();
            InitializeComponent();
            Instance = this;
            LogMessages = new ObservableCollection<string>();
            DataContext = this;

            Credentials = UserCredentials.Load();
            if (Credentials != null)
            {
                username.Text = Credentials.Username;
                password.Password = Credentials.Password;
                autoLoginCheckBox.IsChecked = Credentials.AutoLogin;
                autoSelectBatchCheckBox.IsChecked = Credentials.AutoSelectBatch;

                if (Credentials.AutoLogin)
                {
                    _ = LoginAsync();
                }
                return;
            }

            Credentials = new UserCredentials();
            Credentials.Username = username.Text;
            Credentials.Password = password.Password;
            Credentials.AutoLogin = autoLoginCheckBox.IsChecked ?? false;
            Credentials.AutoSelectBatch = autoSelectBatchCheckBox.IsChecked ?? false;
            Credentials.Save();
        }

        private void ShowDisclaimer()
        {
            DisclaimerWindow disclaimerWindow = new DisclaimerWindow();
            disclaimerWindow.ShowDialog();

            if (!disclaimerWindow.IsAgreed)
            {
                throw new Exception("Disclaimer not agreed.");
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (isLogged)
            {
                WriteLine("请勿重复登录！");
                return;
            }

            _ = LoginAsync();
        }

        public async Task LoginAsync()
        {
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

        public async Task StartSelectClass(BatchInfo batch)
        {
            await web.SetBatchIDAsync(batch);
            var list = await web.GetFavoriteCoursesAsync();
            web.KeepOnline();

            var tasks = list.Select(async course =>
            {
                var (isSuccess, msg) = await web.SelectCourseAsync(course);
                return new { course.courseName, isSuccess, msg };
            }).ToList();

            var results = await Task.WhenAll(tasks);

            WriteLine("选课完成!");

            var failedCourses = results.Where(result => !result.isSuccess).ToList();
            var successfulCount = results.Count(result => result.isSuccess);

            foreach (var result in failedCourses)
            {
                WriteLine($"课程选择失败: {result.courseName}, 原因: {result.msg}");
            }

            WriteLine($"选择成功课程的数目: {successfulCount}");
        }

        public void WriteLine<T>(T msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{time}] : {msg}";

            Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    LogMessages.Add(logMessage);
                    if (ConsoleScrollViewer.VerticalOffset == ConsoleScrollViewer.ScrollableHeight)
                    {
                        ConsoleScrollViewer.ScrollToEnd();
                    }
                });
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void username_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                password.Focus();
            }
        }

        private void password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        private void autoSelectBatchCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Credentials.AutoSelectBatch = autoSelectBatchCheckBox.IsChecked ?? false;
            Credentials.Save();
        }

        private void autoLoginCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Credentials.AutoLogin = autoLoginCheckBox.IsChecked ?? false;
            Credentials.Save();
        }
    }
}
