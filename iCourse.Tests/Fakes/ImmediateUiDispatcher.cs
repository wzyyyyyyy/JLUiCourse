using iCourse.Services;

namespace iCourse.Tests.Fakes;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        action();
    }
}
