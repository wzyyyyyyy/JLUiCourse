using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
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

        [ObservableProperty]
        private Visibility afterLoginButtonVisibility = Visibility.Hidden;

        private BatchInfo batch;

        public MainWindowViewModel()
        {
            var credentials = App.ServiceProvider.GetService<UserCredentials>();

            AutoLogin = credentials.AutoLogin;
            AutoSelectBatch = credentials.AutoSelectBatch;

            if (credentials.AutoLogin && !string.IsNullOrEmpty(credentials.Username) &&
                !string.IsNullOrEmpty(credentials.Password))
            {
                Username = credentials.Username;
                Password = credentials.Password;
                Login();
            }

            WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, LoginSuccess);
            WeakReferenceMessenger.Default.Register<SelectCourseFinishedMessage>(this, SelectCourseFinished);

            WeakReferenceMessenger.Default.Register<SetBatchFinishedMessage>(this,
                (object recipient, SetBatchFinishedMessage message) =>
                {
                    batch = message.BatchInfo;
                });
        }

        [RelayCommand]
        private void Login()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                MessageBox.Show("请输入账号或密码!");
                return;
            }

            _ = App.ServiceProvider.GetService<JLUiCourseApi>().LoginAsync(Username, Password);
        }

        [RelayCommand]
        private void StartSelectCourse()
        {
            App.ServiceProvider.GetService<JLUiCourseApi>().StartSelectClassAsync();
        }

        [RelayCommand]
        private void QueryCourses()
        {
            WeakReferenceMessenger.Default.Send(new ShowWindowMessage(typeof(QueryCourseWindowViewModel)));
        }

        private void LoginSuccess(object recipient, LoginSuccessMessage message)
        {
            CanLogin = false;
            AfterLoginButtonVisibility = Visibility.Visible;
        }

        private void SelectCourseFinished(object recipient, SelectCourseFinishedMessage message)
        {
            if (ProgressBarVisibility != Visibility.Visible) ProgressBarVisibility = Visibility.Visible;
            ProgressValue = ((double)message.FinishedNum / message.Total) * 100;
        }


        public void Receive(PropertyChangedMessage<string> message)
        {
            switch (message.PropertyName)
            {
                case nameof(Username):
                    App.ServiceProvider.GetService<UserCredentials>().Username = Username;
                    break;
                case nameof(Password):
                    App.ServiceProvider.GetService<UserCredentials>().Password = Password;
                    break;
            }
        }

        public void Receive(PropertyChangedMessage<bool> message)
        {
            switch (message.PropertyName)
            {
                case nameof(AutoLogin):
                    App.ServiceProvider.GetService<UserCredentials>().AutoLogin = AutoLogin;
                    break;
                case nameof(AutoSelectBatch):
                    App.ServiceProvider.GetService<UserCredentials>().AutoSelectBatch = AutoSelectBatch;
                    break;
            }
        }
    }
}
