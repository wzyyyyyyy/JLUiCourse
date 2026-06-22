using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace iCourse.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IJLUiCourseApi api;
    private readonly UserCredentials credentials;
    private readonly IDialogService dialogs;
    private readonly IUiDispatcher dispatcher;
    private readonly Dictionary<string, CourseSelectionStatusItem> statusByCourseId = [];
    private int startInProgress;

    [ObservableProperty]
    private bool canLogin = true;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private bool autoLogin;

    [ObservableProperty]
    private bool autoSelectBatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSelection))]
    [NotifyCanExecuteChangedFor(nameof(StartSelectCourseCommand))]
    private bool areAfterLoginButtonsVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartSelection))]
    [NotifyCanExecuteChangedFor(nameof(StartSelectCourseCommand))]
    private bool isSelectionRunning;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int runningCount;

    [ObservableProperty]
    private int succeededCount;

    [ObservableProperty]
    private int failedCount;

    [ObservableProperty]
    private string bannerText = string.Empty;

    [ObservableProperty]
    private bool isBannerVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerInfo))]
    [NotifyPropertyChangedFor(nameof(IsBannerWarning))]
    [NotifyPropertyChangedFor(nameof(IsBannerError))]
    private SystemBannerSeverity bannerSeverity;

    public MainWindowViewModel(
        IJLUiCourseApi api,
        UserCredentials credentials,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        IMessenger messenger)
    {
        this.api = api;
        this.credentials = credentials;
        this.dialogs = dialogs;
        this.dispatcher = dispatcher;

        AutoLogin = credentials.AutoLogin;
        AutoSelectBatch = credentials.AutoSelectBatch;

        messenger.Register<LoginSuccessMessage>(this, LoginSuccess);
        messenger.Register<CourseSelectionRunStartedMessage>(this, CourseSelectionRunStarted);
        messenger.Register<CourseSelectionStatusChangedMessage>(this, CourseSelectionStatusChanged);
        messenger.Register<CourseSelectionRunCompletedMessage>(this, CourseSelectionRunCompleted);
        messenger.Register<SystemBannerMessage>(this, SystemBannerReceived);

        if (credentials.AutoLogin && !string.IsNullOrEmpty(credentials.Username) && !string.IsNullOrEmpty(credentials.Password))
        {
            Username = credentials.Username;
            Password = credentials.Password;
            _ = Login();
        }
    }

    public ObservableCollection<CourseSelectionStatusItem> CourseStatuses { get; } = [];

    public bool CanStartSelection => AreAfterLoginButtonsVisible && !IsSelectionRunning;

    public bool IsBannerInfo => BannerSeverity == SystemBannerSeverity.Info;

    public bool IsBannerWarning => BannerSeverity == SystemBannerSeverity.Warning;

    public bool IsBannerError => BannerSeverity == SystemBannerSeverity.Error;

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

    [RelayCommand(CanExecute = nameof(CanStartSelection))]
    private async Task StartSelectCourse()
    {
        if (!CanStartSelection || Interlocked.CompareExchange(ref startInProgress, 1, 0) != 0)
        {
            return;
        }

        dispatcher.Post(() =>
        {
            IsSelectionRunning = true;
            ShowBanner("正在读取收藏课程…", SystemBannerSeverity.Info);
        });

        try
        {
            await api.StartSelectClassAsync();
        }
        finally
        {
            dispatcher.Post(() =>
            {
                IsSelectionRunning = false;
                Interlocked.Exchange(ref startInProgress, 0);
            });
        }
    }

    [RelayCommand]
    private void StopSelectCourse()
    {
        api.StopSelectClass();
    }

    [RelayCommand]
    private void QueryCourses()
    {
        _ = dialogs.ShowQueryCoursesAsync();
    }

    private void LoginSuccess(object recipient, LoginSuccessMessage message)
    {
        dispatcher.Post(() =>
        {
            CanLogin = false;
            AreAfterLoginButtonsVisible = true;
        });
    }

    private void CourseSelectionRunStarted(object recipient, CourseSelectionRunStartedMessage message)
    {
        dispatcher.Post(() =>
        {
            CourseStatuses.Clear();
            statusByCourseId.Clear();

            foreach (var snapshot in message.Snapshots)
            {
                if (statusByCourseId.ContainsKey(snapshot.CourseId))
                {
                    continue;
                }

                var row = new CourseSelectionStatusItem(snapshot);
                statusByCourseId.Add(row.CourseId, row);
                CourseStatuses.Add(row);
            }

            IsSelectionRunning = true;
            IsProgressVisible = CourseStatuses.Count > 0;
            ClearBanner();
            RecalculateSummary();
        });
    }

    private void CourseSelectionStatusChanged(object recipient, CourseSelectionStatusChangedMessage message)
    {
        dispatcher.Post(() =>
        {
            if (statusByCourseId.TryGetValue(message.Snapshot.CourseId, out var row))
            {
                row.Apply(message.Snapshot);
                RecalculateSummary();
            }
        });
    }

    private void CourseSelectionRunCompleted(object recipient, CourseSelectionRunCompletedMessage message)
    {
        dispatcher.Post(() =>
        {
            IsSelectionRunning = false;
            if (message.WasCancelled)
            {
                ShowBanner("选课任务已停止", SystemBannerSeverity.Warning);
            }
        });
    }

    private void SystemBannerReceived(object recipient, SystemBannerMessage message)
    {
        dispatcher.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                ClearBanner();
                return;
            }

            ShowBanner(message.Text, message.Severity);
        });
    }

    private void RecalculateSummary()
    {
        TotalCount = CourseStatuses.Count;
        RunningCount = CourseStatuses.Count(row => row.State is
            CourseSelectionState.Waiting or
            CourseSelectionState.Racing or
            CourseSelectionState.BackingOff);
        SucceededCount = CourseStatuses.Count(row => row.State == CourseSelectionState.Succeeded);
        FailedCount = CourseStatuses.Count(row => row.State == CourseSelectionState.Failed);

        var finalCount = CourseStatuses.Count(row => row.State is
            CourseSelectionState.Succeeded or
            CourseSelectionState.Failed or
            CourseSelectionState.Cancelled);
        ProgressValue = TotalCount == 0 ? 0 : (double)finalCount / TotalCount * 100;
    }

    private void ShowBanner(string text, SystemBannerSeverity severity)
    {
        BannerText = text;
        BannerSeverity = severity;
        IsBannerVisible = true;
    }

    private void ClearBanner()
    {
        BannerText = string.Empty;
        BannerSeverity = SystemBannerSeverity.Info;
        IsBannerVisible = false;
    }

    partial void OnUsernameChanged(string value) => credentials.Username = value;

    partial void OnPasswordChanged(string value) => credentials.Password = value;

    partial void OnAutoLoginChanged(bool value) => credentials.AutoLogin = value;

    partial void OnAutoSelectBatchChanged(bool value) => credentials.AutoSelectBatch = value;
}
