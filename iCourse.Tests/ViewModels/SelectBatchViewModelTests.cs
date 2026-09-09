using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.Services;
using iCourse.Tests.Fakes;
using iCourse.ViewModels;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;

namespace iCourse.Tests.ViewModels;

[Collection("Headless UI")]
public sealed class SelectBatchViewModelTests(HeadlessFixture app)
{
    [Fact]
    public Task ConfirmSelection_ReturnsBatchWithoutSubmittingOrPersistingIt() => app.Run(async () =>
    {
        using var fixture = new DialogFixture();
        var batch = new BatchInfo { batchId = "new-batch", batchName = "新批次" };
        var viewModel = fixture.CreateViewModel([batch]);
        var owner = new Window();
        var window = new SelectBatchWindow { DataContext = viewModel };
        try
        {
            owner.Show();
            var result = window.ShowDialog<BatchInfo?>(owner);
            var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>());
            list.SelectedItem = batch;
            Assert.Same(batch, viewModel.SelectedBatch);

            await viewModel.ConfirmSelectionCommand.ExecuteAsync(window);

            Assert.Same(batch, await result.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.Equal(0, fixture.Handler.CallCount);
            Assert.Equal("previous-batch", fixture.Credentials.LastBatchId);
            Assert.Empty(fixture.Dialogs.Messages);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    });

    [Fact]
    public Task ConfirmSelection_WithoutSelectionKeepsDialogOpenAndShowsPrompt() => app.Run(async () =>
    {
        using var fixture = new DialogFixture();
        var viewModel = fixture.CreateViewModel([]);
        var owner = new Window();
        var window = new SelectBatchWindow { DataContext = viewModel };
        try
        {
            owner.Show();
            var result = window.ShowDialog<BatchInfo?>(owner);

            await viewModel.ConfirmSelectionCommand.ExecuteAsync(window);

            Assert.False(result.IsCompleted);
            Assert.Equal(("提示", "请选择一个批次"), Assert.Single(fixture.Dialogs.Messages));
            Assert.Equal(0, fixture.Handler.CallCount);
            Assert.Equal("previous-batch", fixture.Credentials.LastBatchId);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    });

    private sealed class DialogFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly Logger logger;
        private readonly Http http;
        private readonly ServiceProvider services;

        public DialogFixture()
        {
            Directory.CreateDirectory(Path.Combine(root, "Logs"));
            var paths = new FakeAppPaths(root);
            Credentials = new UserCredentials(paths) { LastBatchId = "previous-batch" };
            logger = new Logger(paths);
            logger.Initialize();
            http = new Http(TimeSpan.FromSeconds(1), logger, Handler);
            var api = new JLUiCourseApi(logger, Credentials, Dialogs, null!, null!, new StrongReferenceMessenger());
            typeof(JLUiCourseApi).GetField("client", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(api, http);
            typeof(JLUiCourseApi).GetField("token", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(api, "test-token");
            services = new ServiceCollection()
                .AddSingleton(Credentials)
                .AddSingleton<IDialogService>(Dialogs)
                .AddSingleton(api)
                .BuildServiceProvider();
        }

        public UserCredentials Credentials { get; }
        public RecordingHandler Handler { get; } = new();
        public RecordingDialogs Dialogs { get; } = new();

        public SelectBatchViewModel CreateViewModel(IReadOnlyList<BatchInfo> batches) =>
            ActivatorUtilities.CreateInstance<SelectBatchViewModel>(services, batches);

        public void Dispose()
        {
            services.Dispose();
            http.Dispose();
            logger.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            // Stop immediately if the chooser starts the request owned by its caller.
            throw new InvalidOperationException("The batch chooser must only return the selected batch.");
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public List<(string Title, string Message)> Messages { get; } = [];

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<string?> ShowCaptchaAsync(string base64Image) => Task.FromResult<string?>(null);
        public Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches) => Task.FromResult<BatchInfo?>(null);
        public Task ShowQueryCoursesAsync() => Task.CompletedTask;
    }
}
