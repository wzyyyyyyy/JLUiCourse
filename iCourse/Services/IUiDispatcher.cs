using System;

namespace iCourse.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
