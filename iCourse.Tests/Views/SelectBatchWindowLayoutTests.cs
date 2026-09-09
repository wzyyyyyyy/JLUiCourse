using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using iCourse.Models;
using iCourse.Views;

namespace iCourse.Tests.Views;

[Collection("Headless UI")]
public sealed class SelectBatchWindowLayoutTests(HeadlessFixture app)
{
    [Theory]
    [InlineData(600, 465, 14, 1)]
    [InlineData(480, 320, 14, 1.25)]
    [InlineData(480, 320, 20, 1.5)]
    [InlineData(760, 560, 24, 2)]
    public Task ConfirmButton_RemainsVisibleAndClickable(
        double width, double height, double fontSize, double scaling) => app.Run(() =>
    {
        var viewModel = new BatchDialogData();
        var window = new SelectBatchWindow
        {
            DataContext = viewModel,
            FontSize = fontSize,
            Width = width,
            Height = height
        };
        try
        {
            window.Show();
            window.SetRenderScaling(scaling);
            window.UpdateLayout();
            Assert.Equal(new Size(width, height), window.ClientSize);

            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                candidate => candidate.Content as string == "确定");
            var bounds = BoundsInWindow(button, window);

            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            Assert.True(new Rect(window.ClientSize).Contains(bounds),
                $"Confirmation bounds {bounds} exceed client size {window.ClientSize}.");

            window.MouseDown(bounds.Center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(bounds.Center, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, viewModel.ConfirmCount);
            Assert.Same(window, viewModel.ConfirmedWindow);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task LongDetails_CanScrollToLastFieldWithoutMovingConfirmation() => app.Run(() =>
    {
        var viewModel = new BatchDialogData();
        var window = new SelectBatchWindow
        {
            DataContext = viewModel,
            Width = 480,
            Height = 320,
            FontSize = 20
        };
        try
        {
            window.Show();
            window.UpdateLayout();

            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                candidate => candidate.Content as string == "确定");
            var initialButtonBounds = BoundsInWindow(button, window);
            var lastLabel = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "是否可选:");
            var detailsScroller = Assert.Single(lastLabel.GetVisualAncestors().OfType<ScrollViewer>());
            Assert.True(detailsScroller.Extent.Height > detailsScroller.Viewport.Height);
            Assert.True(detailsScroller.Extent.Width <= detailsScroller.Viewport.Width + 1);
            var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>());
            var batchName = Assert.Single(list.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == viewModel.SelectedBatch.batchName);
            Assert.True(batchName.TextLayout.TextLines.Count > 1);
            Assert.True(batchName.Bounds.Width <= list.Bounds.Width);

            detailsScroller.ScrollToEnd();
            window.UpdateLayout();

            Assert.True(detailsScroller.Offset.Y > 0);
            var scrollBounds = BoundsInWindow(detailsScroller, window);
            Assert.True(scrollBounds.Contains(BoundsInWindow(lastLabel, window)));
            Assert.Equal(initialButtonBounds, BoundsInWindow(button, window));
            Assert.True(scrollBounds.Bottom <= initialButtonBounds.Top);
        }
        finally
        {
            window.Close();
        }
    });

    private static Rect BoundsInWindow(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    public sealed class BatchDialogData
    {
        public BatchDialogData()
        {
            ConfirmSelectionCommand = new RelayCommand<Window>(window =>
            {
                ConfirmCount++;
                ConfirmedWindow = window;
            });
        }

        public IReadOnlyList<BatchInfo> BatchList { get; } =
        [
            new()
            {
                batchId = "batch-with-a-long-identifier-2026-2027-first-semester",
                batchName = "2026—2027学年第一学期本科生公共选修课选课批次（补退选阶段）",
                beginTime = "2026-09-09 08:00:00",
                endTime = "2026-09-30 20:00:00",
                tacticName = "按培养方案开放选课，请核对校区、课程时间和容量限制。",
                noSelectReason = string.Concat(Enumerable.Repeat("当前未到选课时间，请在开放后重新选择该批次。", 8)),
                typeName = "公共选修课及跨专业课程",
                canSelect = false
            }
        ];

        public BatchInfo SelectedBatch => BatchList[0];
        public IRelayCommand<Window> ConfirmSelectionCommand { get; }
        public int ConfirmCount { get; private set; }
        public Window? ConfirmedWindow { get; private set; }
    }
}
