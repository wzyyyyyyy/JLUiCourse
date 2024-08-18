using Newtonsoft.Json;
using System.IO;

namespace iCourse.Models
{
    public class UserCredentials
    {
        private const string CredentialsFilePath = "credentials.json";

        public string Username { get; init; }
        public string Password { get; init; }
        public string LastBatchId { get; set; }
        public bool AutoLogin { get; init; }
        public bool AutoSelectBatch { get; init; }

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