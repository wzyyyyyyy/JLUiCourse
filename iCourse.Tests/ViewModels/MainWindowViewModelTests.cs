using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.Services;
using iCourse.Tests.Fakes;
using iCourse.ViewModels;

namespace iCourse.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void InitialState_HidesProgressAndOnlyAllowsLogin()
    {
        var (viewModel, _, _) = CreateViewModel();

        Assert.False(viewModel.IsProgressVisible);
        Assert.Equal(0, viewModel.ProgressValue);
        Assert.True(viewModel.CanLogin);
        Assert.False(viewModel.AreAfterLoginButtonsVisible);
        Assert.False(viewModel.CanStartSelection);
        Assert.Empty(viewModel.CourseStatuses);
    }

    [Fact]
    public void RunStartedAndRepeatedStatusChanges_KeepOneStableRowPerCourse()
    {
        var (viewModel, messenger, _) = CreateViewModel();
        messenger.Send(new CourseSelectionRunStartedMessage(
            [Snapshot("course-1", CourseSelectionState.Waiting, version: 0)]));

        var originalRow = Assert.Single(viewModel.CourseStatuses);

        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("course-1", CourseSelectionState.Racing, attemptCount: 1, version: 1)));
        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("course-1", CourseSelectionState.BackingOff, attemptCount: 2, version: 2)));
        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("unknown", CourseSelectionState.Racing, version: 1)));

        Assert.Same(originalRow, Assert.Single(viewModel.CourseStatuses));
        Assert.Equal(CourseSelectionState.BackingOff, originalRow.State);
        Assert.Equal("退避中", originalRow.StateText);
        Assert.Equal(2, originalRow.AttemptCount);
    }

    [Fact]
    public void RacingToSucceeded_UpdatesDetailsSummaryAndProgress()
    {
        var (viewModel, messenger, _) = CreateViewModel();
        messenger.Send(new CourseSelectionRunStartedMessage(
            [Snapshot("course-1", CourseSelectionState.Waiting, version: 0)]));
        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("course-1", CourseSelectionState.Racing, attemptCount: 2, elapsedSeconds: 0.4, latestResult: "正在尝试", version: 1)));
        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("course-1", CourseSelectionState.Succeeded, attemptCount: 3, elapsedSeconds: 1.24, latestResult: "选课成功", version: 2)));

        var row = Assert.Single(viewModel.CourseStatuses);
        Assert.Equal("成功", row.StateText);
        Assert.Equal(3, row.AttemptCount);
        Assert.Equal("1.2 秒", row.ElapsedText);
        Assert.Equal("选课成功", row.LatestResult);
        Assert.True(row.IsSucceeded);
        Assert.False(row.IsRacing);
        Assert.Equal(1, viewModel.TotalCount);
        Assert.Equal(0, viewModel.RunningCount);
        Assert.Equal(1, viewModel.SucceededCount);
        Assert.Equal(0, viewModel.FailedCount);
        Assert.Equal(100, viewModel.ProgressValue);
    }

    [Fact]
    public void MixedCourseStates_CalculateSummaryAndFinalProgress()
    {
        var (viewModel, messenger, _) = CreateViewModel();
        messenger.Send(new CourseSelectionRunStartedMessage(
        [
            Snapshot("success", CourseSelectionState.Waiting, version: 0),
            Snapshot("failure", CourseSelectionState.Waiting, version: 0),
            Snapshot("running", CourseSelectionState.Waiting, version: 0),
            Snapshot("cancelled", CourseSelectionState.Waiting, version: 0)
        ]));

        messenger.Send(new CourseSelectionStatusChangedMessage(Snapshot("success", CourseSelectionState.Succeeded, version: 1)));
        messenger.Send(new CourseSelectionStatusChangedMessage(Snapshot("failure", CourseSelectionState.Failed, version: 1)));
        messenger.Send(new CourseSelectionStatusChangedMessage(Snapshot("running", CourseSelectionState.Racing, version: 1)));
        messenger.Send(new CourseSelectionStatusChangedMessage(Snapshot("cancelled", CourseSelectionState.Cancelled, version: 1)));

        Assert.Equal(4, viewModel.TotalCount);
        Assert.Equal(1, viewModel.RunningCount);
        Assert.Equal(1, viewModel.SucceededCount);
        Assert.Equal(1, viewModel.FailedCount);
        Assert.Equal(75, viewModel.ProgressValue);
        Assert.True(viewModel.IsProgressVisible);
    }

    [Fact]
    public void StatusItem_IgnoresStaleAndPostFinalRegressionsButAcceptsSameFinalRefresh()
    {
        var item = new CourseSelectionStatusItem(
            Snapshot("course-1", CourseSelectionState.Waiting, latestResult: "等待开始", version: 0));
        var changedProperties = new List<string?>();
        item.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        item.Apply(Snapshot("course-1", CourseSelectionState.Racing, attemptCount: 2, version: 2));
        item.Apply(Snapshot("course-1", CourseSelectionState.BackingOff, attemptCount: 1, version: 1));

        Assert.Equal(CourseSelectionState.Racing, item.State);
        Assert.Equal(2, item.Version);

        item.Apply(Snapshot("course-1", CourseSelectionState.Succeeded, latestResult: "完成", version: 3));
        item.Apply(Snapshot("course-1", CourseSelectionState.Racing, latestResult: "不应回退", version: 4));

        Assert.Equal(CourseSelectionState.Succeeded, item.State);
        Assert.Equal("完成", item.LatestResult);
        Assert.Equal(3, item.Version);

        item.Apply(Snapshot("course-1", CourseSelectionState.Succeeded, attemptCount: 3, latestResult: "完成（已确认）", version: 4));

        Assert.Equal(4, item.Version);
        Assert.Equal("完成（已确认）", item.LatestResult);
        Assert.Contains(nameof(CourseSelectionStatusItem.StateText), changedProperties);
        Assert.Contains(nameof(CourseSelectionStatusItem.IsSucceeded), changedProperties);
    }

    [Fact]
    public async Task SelectionMessages_PostToDispatcherBeforeMutatingUiState()
    {
        var dispatcher = new RecordingDispatcher();
        var (viewModel, messenger, _) = CreateViewModel(dispatcher: dispatcher);
        var senderThreadId = -1;

        await Task.Run(() =>
        {
            senderThreadId = Environment.CurrentManagedThreadId;
            messenger.Send(new CourseSelectionRunStartedMessage(
                [Snapshot("course-1", CourseSelectionState.Waiting, version: 0)]));
        });

        Assert.Equal(1, dispatcher.PostCount);
        Assert.Equal(senderThreadId, Assert.Single(dispatcher.PostingThreadIds));
        Assert.Empty(viewModel.CourseStatuses);
        dispatcher.Drain();
        Assert.Single(viewModel.CourseStatuses);

        messenger.Send(new CourseSelectionStatusChangedMessage(
            Snapshot("course-1", CourseSelectionState.Racing, version: 1)));
        Assert.Equal(2, dispatcher.PostCount);
        Assert.Equal(CourseSelectionState.Waiting, viewModel.CourseStatuses[0].State);
        dispatcher.Drain();
        Assert.Equal(CourseSelectionState.Racing, viewModel.CourseStatuses[0].State);

        messenger.Send(new SystemBannerMessage("请稍候", SystemBannerSeverity.Warning));
        Assert.Equal(3, dispatcher.PostCount);
        Assert.False(viewModel.IsBannerVisible);
        dispatcher.Drain();
        Assert.True(viewModel.IsBannerVisible);

        messenger.Send(new CourseSelectionRunCompletedMessage(false));
        Assert.Equal(4, dispatcher.PostCount);
        Assert.True(viewModel.IsSelectionRunning);
        dispatcher.Drain();
        Assert.False(viewModel.IsSelectionRunning);
    }

    [Fact]
    public void BannerMessages_RunStartAndCancellation_UpdateBannerState()
    {
        var (viewModel, messenger, _) = CreateViewModel();

        messenger.Send(new SystemBannerMessage("网络拥堵", SystemBannerSeverity.Warning));
        Assert.True(viewModel.IsBannerVisible);
        Assert.Equal("网络拥堵", viewModel.BannerText);

        messenger.Send(new CourseSelectionRunStartedMessage(
            [Snapshot("course-1", CourseSelectionState.Waiting, version: 0)]));
        Assert.False(viewModel.IsBannerVisible);
        Assert.Equal(string.Empty, viewModel.BannerText);

        messenger.Send(new CourseSelectionRunCompletedMessage(true));
        Assert.True(viewModel.IsBannerVisible);
        Assert.Equal("选课任务已停止", viewModel.BannerText);

        messenger.Send(new SystemBannerMessage(string.Empty));
        Assert.False(viewModel.IsBannerVisible);
    }

    [Fact]
    public async Task StartPreventsDuplicatesStopDelegatesAndCanStartTracksLoginAndRun()
    {
        var api = new FakeApi();
        api.HoldStartOpen();
        var (viewModel, messenger, _) = CreateViewModel(api: api);

        messenger.Send(new LoginSuccessMessage());
        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.AreAfterLoginButtonsVisible);
        Assert.True(viewModel.CanStartSelection);

        var firstStart = viewModel.StartSelectCourseCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsSelectionRunning);
        Assert.False(viewModel.CanStartSelection);

        await viewModel.StartSelectCourseCommand.ExecuteAsync(null);
        Assert.Equal(1, api.StartCallCount);

        viewModel.StopSelectCourseCommand.Execute(null);
        Assert.Equal(1, api.StopCallCount);

        api.CompleteStart();
        await firstStart;

        Assert.False(viewModel.IsSelectionRunning);
        Assert.True(viewModel.CanStartSelection);
    }

    [Fact]
    public void EmptyRun_HasZeroProgressAndHiddenProgressBar()
    {
        var (viewModel, messenger, _) = CreateViewModel();

        messenger.Send(new CourseSelectionRunStartedMessage([]));

        Assert.Equal(0, viewModel.TotalCount);
        Assert.Equal(0, viewModel.ProgressValue);
        Assert.False(viewModel.IsProgressVisible);
    }

    private static (MainWindowViewModel ViewModel, WeakReferenceMessenger Messenger, FakeApi Api) CreateViewModel(
        FakeApi? api = null,
        IUiDispatcher? dispatcher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        api ??= new FakeApi();
        dispatcher ??= new ImmediateUiDispatcher();
        var messenger = new WeakReferenceMessenger();
        var credentials = new UserCredentials(new FakeAppPaths(root));
        var viewModel = new MainWindowViewModel(
            api,
            credentials,
            new FakeDialogService(),
            dispatcher,
            messenger);

        return (viewModel, messenger, api);
    }

    private static CourseSelectionSnapshot Snapshot(
        string courseId,
        CourseSelectionState state,
        int attemptCount = 0,
        double elapsedSeconds = 0,
        string latestResult = "状态更新",
        long version = 0) =>
        new(
            courseId,
            $"课程 {courseId}",
            state,
            attemptCount,
            TimeSpan.FromSeconds(elapsedSeconds),
            latestResult,
            version);

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> actions = new();

        public int PostCount { get; private set; }
        public List<int> PostingThreadIds { get; } = [];

        public void Post(Action action)
        {
            PostCount++;
            PostingThreadIds.Add(Environment.CurrentManagedThreadId);
            actions.Enqueue(action);
        }

        public void Drain()
        {
            while (actions.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class FakeApi : IJLUiCourseApi
    {
        private TaskCompletionSource<bool>? startCompletion;

        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }

        public Task AddToFavoritesAsync(Course course) => Task.CompletedTask;
        public Task LoginAsync(string username, string password) => Task.CompletedTask;
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize) => Task.FromResult(new List<Course>());
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key) => Task.FromResult(new List<Course>());
        public Task SetBatchIdAsync(BatchInfo batch) => Task.CompletedTask;

        public Task StartSelectClassAsync()
        {
            StartCallCount++;
            return startCompletion?.Task ?? Task.CompletedTask;
        }

        public void StopSelectClass()
        {
            StopCallCount++;
        }

        public void HoldStartOpen()
        {
            startCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void CompleteStart()
        {
            startCompletion?.SetResult(true);
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches) => Task.FromResult<BatchInfo?>(null);
        public Task<string?> ShowCaptchaAsync(string base64Image) => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task ShowQueryCoursesAsync() => Task.CompletedTask;
    }
}
