using iCourse.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task<string?> ShowCaptchaAsync(string base64Image);
    Task<BatchInfo?> SelectBatchAsync(IReadOnlyList<BatchInfo> batches);
    Task ShowQueryCoursesAsync();
}
