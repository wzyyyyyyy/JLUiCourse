using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualBasic;

namespace iCourse.ViewModels
{
    partial class MainWindowViewModel : 
        ObservableObject,
        IRecipient<PropertyChangedMessage<string>>
    {
        private Web web;
        private bool isLogged;

        public ObservableCollection<string> LogMessages { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private string username;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private string password;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool autoLogin;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool autoSelectBatch;

        public MainWindowViewModel()
        {
            var credentials = UserCredentials.Load();
            if (credentials != null)
            {
                Username = credentials.Username;
                Password = credentials.Password;
                AutoLogin = credentials.AutoLogin;
                AutoSelectBatch = credentials.AutoSelectBatch;

                if (AutoLogin)
                {
                    _ = LoginAsync();
                }
            }
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (isLogged)
            {
                WriteLine("请勿重复登录！");
                return;
            }

            web = new Web(Username, Password);

            var (code, msg) = await web.LoginAsync();
            var response = web.GetLoginResponse();

            if (code != 200)
            {
                WriteLine(msg);
                WriteLine("登录失败，请检查用户名和密码是否正确。");
                isLogged = false;
                return;
            }

            WriteLine(msg);
            isLogged = true;

            var studentName = response["data"]["student"]["XM"].ToString();
            var studentID = response["data"]["student"]["XH"].ToString();
            var collage = response["data"]["student"]["YXMC"].ToString();

            WriteLine($"姓名：{studentName}");
            WriteLine($"学号：{studentID}");
            WriteLine($"学院：{collage}");

            var batchInfos = web.GetBatchInfo();
            var selectBatchWindow = new SelectBatchWindow(batchInfos);
            selectBatchWindow.ShowDialog();
        }

        [RelayCommand]
        private async Task StartSelectClassAsync(BatchInfo batch)
        {
            await web.SetBatchIdAsync(batch);
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

        private void WriteLine<T>(T msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{time}] : {msg}";

            App.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(logMessage);
            });
        }

        public void Receive(PropertyChangedMessage<string> message)
        {
            if (message.Sender is MainWindowViewModel vm)
            {
                SaveCredentials();
            }
        }

        public void SaveCredentials()
        {
            var credentials = new UserCredentials
            {
                Username = Username,
                Password = Password,
                AutoLogin = AutoLogin,
                AutoSelectBatch = AutoSelectBatch
            };
            credentials.Save();
        }
    }
}
