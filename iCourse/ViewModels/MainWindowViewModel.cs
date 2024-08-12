using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace iCourse.ViewModels
{
    partial class MainWindowViewModel :
        ObservableRecipient,
        IRecipient<PropertyChangedMessage<string>>,
        IRecipient<PropertyChangedMessage<bool>>
    {
        [ObservableProperty]
        private bool canLogin = true;

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
                    Login();
                }
            }

            Task.Run(async () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    await Task.Delay(100);
                    Logger.WriteLine($"test{i}");
                }
            });

            WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, LoginSuccess);
        }

        [RelayCommand]
        private void Login()
        {
            _ = App.ServiceProvider.GetService<Web>().LoginAsync(Username, Password);
        }

        private void LoginSuccess(object recipient, LoginSuccessMessage message)
        {
            CanLogin = false;
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
