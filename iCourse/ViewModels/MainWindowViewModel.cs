using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace iCourse.ViewModels;

public partial class MainWindowViewModel :
    ObservableRecipient,
    IRecipient<PropertyChangedMessage<string>>,
    IRecipient<PropertyChangedMessage<bool>>
{
    private readonly IJLUiCourseApi api;
    private readonly UserCredentials credentials;
    private readonly IDialogService dialogs;

    [ObservableProperty]
    private bool canLogin = true;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private string username = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool autoLogin;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private bool autoSelectBatch;

    [ObservableProperty]
    private bool areAfterLoginButtonsVisible;

    public MainWindowViewModel(IJLUiCourseApi api, UserCredentials credentials, Logger logger, IDialogService dialogs)
    {
        this.api = api;
        this.credentials = credentials;
        this.dialogs = dialogs;

        LogMessages = logger.LogMessages;

        AutoLogin = credentials.AutoLogin;
        AutoSelectBatch = credentials.AutoSelectBatch;

        WeakReferenceMessenger.Default.Register<LoginSuccessMessage>(this, LoginSuccess);
        WeakReferenceMessenger.Default.Register<SelectCourseFinishedMessage>(this, SelectCourseFinished);

        if (credentials.AutoLogin && !string.IsNullOrEmpty(credentials.Username) && !string.IsNullOrEmpty(credentials.Password))
        {
            Username = credentials.Username;
            Password = credentials.Password;
            _ = Login();
        }
    }

    public ObservableCollection<string> LogMessages { get; }

    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            await dialogs.ShowMessageAsync("提示", "请输入账号或密码!");
            return;
        }

        _ = api.LoginAsync(Username, Password);
    }

    [RelayCommand]
    private void StartSelectCourse()
    {
        _ = api.StartSelectClassAsync();
    }

    [RelayCommand]
    private void QueryCourses()
    {
        _ = dialogs.ShowQueryCoursesAsync();
    }

    private void LoginSuccess(object recipient, LoginSuccessMessage message)
    {
        CanLogin = false;
        AreAfterLoginButtonsVisible = true;
    }

    private void SelectCourseFinished(object recipient, SelectCourseFinishedMessage message)
    {
        IsProgressVisible = true;
        ProgressValue = ((double)message.FinishedNum / message.Total) * 100;
    }

    public void Receive(PropertyChangedMessage<string> message)
    {
        if (message.PropertyName == nameof(Username))
        {
            credentials.Username = Username;
        }

        if (message.PropertyName == nameof(Password))
        {
            credentials.Password = Password;
        }
    }

    public void Receive(PropertyChangedMessage<bool> message)
    {
        if (message.PropertyName == nameof(AutoLogin))
        {
            credentials.AutoLogin = AutoLogin;
        }

        if (message.PropertyName == nameof(AutoSelectBatch))
        {
            credentials.AutoSelectBatch = AutoSelectBatch;
        }
    }
}
