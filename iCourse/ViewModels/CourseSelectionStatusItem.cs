using CommunityToolkit.Mvvm.ComponentModel;
using iCourse.Models;
using System.Globalization;

namespace iCourse.ViewModels;

public sealed class CourseSelectionStatusItem : ObservableObject
{
    private CourseSelectionState state;
    private int attemptCount;
    private string elapsedText = "0.0 秒";
    private string latestResult = string.Empty;
    private long version = -1;

    public CourseSelectionStatusItem(CourseSelectionSnapshot snapshot)
    {
        CourseId = snapshot.CourseId;
        CourseName = snapshot.CourseName;
        Apply(snapshot);
    }

    public string CourseId { get; }

    public string CourseName { get; }

    public CourseSelectionState State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public int AttemptCount
    {
        get => attemptCount;
        private set => SetProperty(ref attemptCount, value);
    }

    public string ElapsedText
    {
        get => elapsedText;
        private set => SetProperty(ref elapsedText, value);
    }

    public string LatestResult
    {
        get => latestResult;
        private set => SetProperty(ref latestResult, value);
    }

    public long Version
    {
        get => version;
        private set => SetProperty(ref version, value);
    }

    public string StateText => State switch
    {
        CourseSelectionState.Waiting => "等待",
        CourseSelectionState.Racing => "竞速中",
        CourseSelectionState.BackingOff => "退避中",
        CourseSelectionState.Succeeded => "成功",
        CourseSelectionState.Failed => "失败",
        CourseSelectionState.Cancelled => "已停止",
        _ => "等待"
    };

    public bool IsWaiting => State is CourseSelectionState.Waiting or CourseSelectionState.Cancelled;

    public bool IsRacing => State == CourseSelectionState.Racing;

    public bool IsBackingOff => State == CourseSelectionState.BackingOff;

    public bool IsSucceeded => State == CourseSelectionState.Succeeded;

    public bool IsFailed => State == CourseSelectionState.Failed;

    public void Apply(CourseSelectionSnapshot snapshot)
    {
        if (snapshot.CourseId != CourseId || snapshot.Version <= Version)
        {
            return;
        }

        if (IsFinal(State) && snapshot.State != State)
        {
            return;
        }

        var stateChanged = State != snapshot.State;

        State = snapshot.State;
        AttemptCount = snapshot.AttemptCount;
        ElapsedText = $"{snapshot.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} 秒";
        LatestResult = snapshot.LatestResult;
        Version = snapshot.Version;

        if (stateChanged)
        {
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsRacing));
            OnPropertyChanged(nameof(IsBackingOff));
            OnPropertyChanged(nameof(IsSucceeded));
            OnPropertyChanged(nameof(IsFailed));
        }
    }

    private static bool IsFinal(CourseSelectionState value) =>
        value is CourseSelectionState.Succeeded
            or CourseSelectionState.Failed
            or CourseSelectionState.Cancelled;
}
