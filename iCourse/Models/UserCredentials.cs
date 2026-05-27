using CommunityToolkit.Mvvm.ComponentModel;
using iCourse.Services;
using Newtonsoft.Json;
using System.IO;
using System.Text;

namespace iCourse.Models;

public partial class UserCredentials(IAppPaths paths) : ObservableObject
{
    private string username = string.Empty;
    private string password = string.Empty;
    private string lastBatchId = string.Empty;
    private bool autoLogin;
    private bool autoSelectBatch;

    public string Username
    {
        get => username;
        set
        {
            if (SetProperty(ref username, value))
            {
                Save();
            }
        }
    }

    public string Password
    {
        get => password;
        set
        {
            if (SetProperty(ref password, value))
            {
                Save();
            }
        }
    }

    public string LastBatchId
    {
        get => lastBatchId;
        set
        {
            if (SetProperty(ref lastBatchId, value))
            {
                Save();
            }
        }
    }

    public bool AutoLogin
    {
        get => autoLogin;
        set
        {
            if (SetProperty(ref autoLogin, value))
            {
                Save();
            }
        }
    }

    public bool AutoSelectBatch
    {
        get => autoSelectBatch;
        set
        {
            if (SetProperty(ref autoSelectBatch, value))
            {
                Save();
            }
        }
    }

    private void Save()
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(paths.CredentialsFilePath, json, Encoding.UTF8);
    }

    public void Load()
    {
        if (!File.Exists(paths.CredentialsFilePath))
        {
            Save();
            return;
        }

        var context = File.ReadAllText(paths.CredentialsFilePath, Encoding.UTF8);
        var loaded = JsonConvert.DeserializeObject<UserCredentialsSnapshot>(context);
        if (loaded is null)
        {
            Save();
            return;
        }

        autoLogin = loaded.AutoLogin;
        username = loaded.Username ?? string.Empty;
        password = loaded.Password ?? string.Empty;
        lastBatchId = loaded.LastBatchId ?? string.Empty;
        autoSelectBatch = loaded.AutoSelectBatch;

        OnPropertyChanged(nameof(AutoLogin));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Password));
        OnPropertyChanged(nameof(LastBatchId));
        OnPropertyChanged(nameof(AutoSelectBatch));
    }

    private sealed class UserCredentialsSnapshot
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? LastBatchId { get; set; }
        public bool AutoLogin { get; set; }
        public bool AutoSelectBatch { get; set; }
    }
}
