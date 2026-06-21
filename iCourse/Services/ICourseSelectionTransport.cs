using iCourse.Models;

namespace iCourse.Services;

public interface ICourseSelectionTransport
{
    Task<CourseSelectionAttempt> SendAsync(Course course, CancellationToken token);
}
