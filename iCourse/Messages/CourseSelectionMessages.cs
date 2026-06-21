using iCourse.Models;

namespace iCourse.Messages;

public sealed record CourseSelectionRunStartedMessage(
    IReadOnlyList<CourseSelectionSnapshot> Snapshots);

public sealed record CourseSelectionStatusChangedMessage(
    CourseSelectionSnapshot Snapshot);

public sealed record CourseSelectionRunCompletedMessage(bool WasCancelled);

public enum SystemBannerSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SystemBannerMessage(
    string Text,
    SystemBannerSeverity Severity = SystemBannerSeverity.Info);
