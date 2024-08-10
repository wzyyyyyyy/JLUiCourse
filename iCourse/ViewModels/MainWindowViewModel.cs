using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Views;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace iCourse.ViewModels
{
    partial class MainWindowViewModel :
        ObservableRecipient,
        IRecipient<PropertyChangedMessage<string>>,
        IRecipient<PropertyChangedMessage<bool>>
    {
        private Web web;
        private bool isLogged;
        private Logger Logger => App.ServiceProvider.GetService<Logger>();
        public ObservableCollection<string> LogMessages => Logger.LogMessages;

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

                if (AutoLogin && Username.Length != 0 && Password.Length != 0)
                {
                    _ = LoginAsync();
                }
            }

            WeakReferenceMessenger.Default.Register<StartSelectClassMessage>(this, StartSelectClassAsync);
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (isLogged)
            {
                Logger.WriteLine("请勿重复登录！");
                return;
            }

            web = new Web(Username, Password);

            var (code, msg) = await web.LoginAsync();
            var response = web.GetLoginResponse();

            if (code != 200)
            {
                Logger.WriteLine(msg);
                Logger.WriteLine("登录失败，请检查用户名和密码是否正确。");
                isLogged = false;
                return;
            }

            Logger.WriteLine(msg);
            isLogged = true;

            var studentName = response["data"]["student"]["XM"].ToString();
            var studentID = response["data"]["student"]["XH"].ToString();
            var collage = response["data"]["student"]["YXMC"].ToString();

            Logger.WriteLine($"姓名：{studentName}");
            Logger.WriteLine($"学号：{studentID}");
            Logger.WriteLine($"学院：{collage}");

            var batchInfos = web.GetBatchInfo();
            var selectBatchWindow = new SelectBatchWindow(batchInfos);
            selectBatchWindow.ShowDialog();
        }

        private async void StartSelectClassAsync(object recipient, StartSelectClassMessage msg)
        {
            await web.SetBatchIdAsync(msg.BatchInfo);
            var list = await web.GetFavoriteCoursesAsync();
            web.KeepOnline();

            var tasks = list.Select(async course =>
            {
                var (isSuccess, msg) = await web.SelectCourseAsync(course);
                return new { course.courseName, isSuccess, msg };
            }).ToList();

            var results = await Task.WhenAll(tasks);

            Logger.WriteLine("选课完成!");

            var failedCourses = results.Where(result => !result.isSuccess).ToList();
            var successfulCount = results.Count(result => result.isSuccess);

            foreach (var result in failedCourses)
            {
                Logger.WriteLine($"课程选择失败: {result.courseName}, 原因: {result.msg}");
            }

            Logger.WriteLine($"选择成功课程的数目: {successfulCount}");
        }

        public void Receive(PropertyChangedMessage<string> message)
        {
            if (message.Sender is MainWindowViewModel)
            {
                SaveCredentials();
            }
        }

        public void Receive(PropertyChangedMessage<bool> message)
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

        public void SaveCredentials()
        {
            if (!AutoLogin) { return; }
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
