using iCourse.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.Services;

public interface IJLUiCourseApi
{
    Task LoginAsync(string username, string password);
    Task StartSelectClassAsync();
    Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize);
    Task<List<Course>> QueryCoursesAsync(int index, int pageMaxSize, string key);
    Task AddToFavoritesAsync(Course course);
    Task SetBatchIdAsync(BatchInfo batch);
}
