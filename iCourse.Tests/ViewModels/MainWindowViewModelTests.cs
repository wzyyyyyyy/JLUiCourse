using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using iCourse.Tests.Fakes;
using iCourse.ViewModels;

namespace iCourse.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void InitialProgressState_IsHidden()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Logs"));

        var paths = new FakeAppPaths(root);
        var logger = new Logger(new ImmediateUiDispatcher(), paths);
        logger.Initialize();
        var credentials = new UserCredentials(paths);

        var viewModel = new MainWindowViewModel(new FakeApi(), credentials, logger, new FakeDialogService());

        Assert.False(viewModel.IsProgressVisible);
        Assert.True(viewModel.CanLogin);
        Assert.False(viewModel.AreAfterLoginButtonsVisible);
    }

    private sealed class FakeApi : IJLUiCourseApi
    {
        public Task AddToFavoritesAsync(Course course) => Task.CompletedTask;
        public Task LoginAsync(string username, string password) => Task.CompletedTask;
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize) => Task.FromResult(new List<Course>());
        public Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key) => Task.FromResult(new List<Course>());
        public Task SetBatchIdAsync(BatchInfo batch) => Task.CompletedTask;
        public Task StartSelectClassAsync() => Task.CompletedTask;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches) => Task.FromResult<BatchInfo?>(null);
        public Task<string?> ShowCaptchaAsync(string base64Image) => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task ShowQueryCoursesAsync() => Task.CompletedTask;
    }
}
