using Newtonsoft.Json;
using System.IO;

namespace iCourse.Models
{
    public class UserCredentials
    {
        private const string CredentialsFilePath = "credentials.json";

        public string Username { get; set; }
        public string Password { get; set; }
        public string LastBatchId { get; set; }
        public bool AutoLogin { get; set; }
        public bool AutoSelectBatch { get; set; }

        public static UserCredentials Load()
        {
            if (File.Exists(CredentialsFilePath))
            {
                var json = File.ReadAllText(CredentialsFilePath);
                return JsonConvert.DeserializeObject<UserCredentials>(json);
            }
            return null;
        }

        public void Save()
        {
            File.WriteAllText(CredentialsFilePath, JsonConvert.SerializeObject(this));
        }
    }
}