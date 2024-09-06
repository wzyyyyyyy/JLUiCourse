using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System.IO;
using System.Text;

namespace iCourse.Models
{
    internal partial class UserCredentials : ObservableObject
    {
        private const string CredentialsFilePath = "credentials.json";

        private string username;
        private string password;
        private string lastBatchId;
        private bool autoLogin;
        private bool autoSelectBatch;

        public string Username
        {
            get => username;
            set
            {
                SetProperty(ref username, value);
                Save();
            }
        }

        public string Password
        {
            get => password;
            set
            {
                SetProperty(ref password, value);
                Save();
            }
        }

        public string LastBatchId
        {
            get => lastBatchId;
            set
            {
                SetProperty(ref lastBatchId, value);
                Save();
            }
        }

        public bool AutoLogin
        {
            get => autoLogin;
            set
            {
                SetProperty(ref autoLogin, value);
                Save();
            }
        }

        public bool AutoSelectBatch
        {
            get => autoSelectBatch;
            set
            {
                SetProperty(ref autoSelectBatch, value);
                Save();
            }
        }

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
    }
}
