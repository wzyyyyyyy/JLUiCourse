namespace iCourse.Services;

public sealed class CourseSelectionRunGuard
{
    private readonly object gate = new();
    private CancellationTokenSource? activeRun;

    public bool TryBegin(out CancellationToken token)
    {
        lock (gate)
        {
            if (activeRun is not null)
            {
                token = default;
                return false;
            }

            activeRun = new CancellationTokenSource();
            token = activeRun.Token;
            return true;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? run;
        lock (gate)
        {
            run = activeRun;
        }

        run?.Cancel();
    }

    public void Complete()
    {
        lock (gate)
        {
            activeRun = null;
        }
    }
}
