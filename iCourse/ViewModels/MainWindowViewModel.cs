using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using HandyControl.Controls;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace iCourse.ViewModels
{
    partial class MainWindowViewModel :
        ObservableRecipient,
        IRecipient<PropertyChangedMessage<string>>,
        IRecipient<PropertyChangedMessage<bool>>
    {
        [ObservableProperty]
        private bool canLogin = true;

        [ObservableProperty]
        private double progressValue;
        [ObservableProperty]
        private Visibility progressBarVisibility = Visibility.Hidden;

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

                if (AutoLogin && !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
                {
                    Login();
                }
            }

            WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, LoginSuccess);
            WeakReferenceMessenger.Default.Register<SelectCourseFinishedMessage>(this, SelectCourseFinished);
        }

        [RelayCommand]
        private void Login()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                MessageBox.Show("请输入账号或密码!");
                return;
            }

            _ = App.ServiceProvider.GetService<Web>().LoginAsync(Username, Password);
        }

        private void LoginSuccess(object recipient, LoginSuccessMessage message)
        {
            CanLogin = false;
        }

        private void SelectCourseFinished(object recipient, SelectCourseFinishedMessage message)
        {
            if (ProgressBarVisibility != Visibility.Visible) ProgressBarVisibility = Visibility.Visible;
            ProgressValue = ((double)message.FinishedNum / message.Total)*100;
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
