using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Windows;

namespace iCourse.Models
{
    partial class UserCredentials :
        ObservableRecipient,
        IRecipient<PropertyChangedMessage<string>>,
        IRecipient<PropertyChangedMessage<bool>>
    {
        private const string CredentialsFilePath = "credentials.json";

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private string username;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private string password;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private string lastBatchId;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool autoLogin;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        private bool autoSelectBatch;

        private void Save()
        {
            File.WriteAllText(CredentialsFilePath, JsonConvert.SerializeObject(this));
        }

        public void Load()
        {
                if (File.Exists(CredentialsFilePath))
                {
                    var context = File.ReadAllText(CredentialsFilePath);
                    var tmp = JsonConvert.DeserializeObject<UserCredentials>(context);
                    AutoLogin = tmp.AutoLogin;
                    Username = tmp.Username;
                    Password = tmp.Password;
                    LastBatchId = tmp.LastBatchId;
                    AutoSelectBatch = tmp.AutoSelectBatch;
                }
                else
                {
                    var file = File.Create(CredentialsFilePath);
                    var json = JsonConvert.SerializeObject(this);
                    file.Write(Encoding.UTF8.GetBytes(json), 0, json.Length);
                    file.Close();
                }
        }

        public void Receive(PropertyChangedMessage<string> message)
        {
            Save();
        }

        public void Receive(PropertyChangedMessage<bool> message)
        {
            Save();
        }
    }
}