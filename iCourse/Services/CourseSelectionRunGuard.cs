namespace iCourse.Services;

public sealed class CourseSelectionRunGuard
{
    private readonly object gate = new();
    private RunState? activeRun;

    public bool TryBegin(out CancellationToken token)
    {
        lock (gate)
        {
            if (activeRun is not null)
            {
                token = default;
                return false;
            }

            activeRun = new RunState();
            token = activeRun.Token;
            return true;
        }
    }

    public void Cancel()
    {
        RunState? run;
        lock (gate)
        {
            run = activeRun;
        }

        run?.TryCancel();
    }

    public void Complete()
    {
        RunState? completed;
        lock (gate)
        {
            completed = activeRun;
            activeRun = null;
        }

        completed?.Complete();
    }

    private sealed class RunState
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource source = new();
        private int cancellationLeases;
        private bool completionRequested;
        private bool disposed;

        public CancellationToken Token => source.Token;

        public void TryCancel()
        {
            lock (gate)
            {
                if (completionRequested)
                {
                    return;
                }

                cancellationLeases++;
            }

            try
            {
                source.Cancel();
            }
            finally
            {
                ReleaseCancellationLease();
            }
        }

        public void Complete()
        {
            var dispose = false;
            lock (gate)
            {
                if (completionRequested)
                {
                    return;
                }

                completionRequested = true;
                if (cancellationLeases == 0)
                {
                    disposed = true;
                    dispose = true;
                }
            }

            if (dispose)
            {
                source.Dispose();
            }
        }

        private void ReleaseCancellationLease()
        {
            var dispose = false;
            lock (gate)
            {
                cancellationLeases--;
                if (completionRequested && cancellationLeases == 0 && !disposed)
                {
                    disposed = true;
                    dispose = true;
                }
            }

            if (dispose)
            {
                source.Dispose();
            }
        }
    }
}
