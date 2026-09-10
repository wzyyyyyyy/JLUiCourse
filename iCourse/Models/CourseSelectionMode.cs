namespace iCourse.Models;

/// <summary>抢课模式。</summary>
public enum CourseSelectionMode
{
    /// <summary>开抢：高频重试，"课容量已满"视为该门课已经没戏，立即放弃。</summary>
    Rush,

    /// <summary>捡漏：低频重试，"课容量已满"后继续等待他人退课。</summary>
    Scavenge
}

/// <summary>在界面与选课服务之间共享当前抢课模式。</summary>
/// <remarks>
/// 界面线程写入、选课泳道后台读取，因此用 <see langword="volatile"/> 字段承载，
/// 切换后从下一次重试开始生效。
/// </remarks>
public sealed class CourseSelectionModeState
{
    private volatile bool scavenge;

    public CourseSelectionMode Mode
    {
        get => scavenge ? CourseSelectionMode.Scavenge : CourseSelectionMode.Rush;
        set => scavenge = value == CourseSelectionMode.Scavenge;
    }

    public bool IsScavenge => scavenge;
}
