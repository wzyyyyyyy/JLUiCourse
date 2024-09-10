using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Helpers;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;

namespace iCourse.ViewModels
{
    partial class QueryCourseWindowViewModel : ObservableObject
    {
        [ObservableProperty] private List<Course> courses;

        [ObservableProperty] private string queryText;

        [ObservableProperty] private bool buttonEnabled;

        [ObservableProperty]
        private int currentPage = 1;

        public QueryCourseWindowViewModel()
        {
            Task.Run(async () =>
            {
                var api = App.ServiceProvider.GetService<JLUiCourseApi>();
                Courses = await api.QueryCoursesAsync(CurrentPage, 15);
                ButtonEnabled = true;
            });
        }

        [RelayCommand]
        private void PreviousPage()
        {
            if (CurrentPage == 1)
            {
                return;
            }
            CurrentPage--;

            ButtonEnabled = false;
            Task.Run(async () =>
            {
                var api = App.ServiceProvider.GetService<JLUiCourseApi>();

                if (string.IsNullOrEmpty(QueryText))
                {
                    Courses = await api.QueryCoursesAsync(CurrentPage, 15);
                    ButtonEnabled = true;
                    return;
                }

                Courses = await api.QueryCoursesAsync(CurrentPage, 15, QueryText);
                ButtonEnabled = true;
            });
        }

        [RelayCommand]
        private void NextPage()
        {
            CurrentPage++;

            ButtonEnabled = false;
            Task.Run(async () =>
            {
                var api = App.ServiceProvider.GetService<JLUiCourseApi>();

                if (string.IsNullOrEmpty(QueryText))
                {
                    Courses = await api.QueryCoursesAsync(CurrentPage, 15);
                    ButtonEnabled = true;
                    return;
                }

                Courses = await api.QueryCoursesAsync(CurrentPage, 15, QueryText);
                ButtonEnabled = true;
            });
        }

        [RelayCommand]
        private void Query()
        {
            ButtonEnabled = false;
            CurrentPage = 1;

            Task.Run(async () =>
            {
                var api = App.ServiceProvider.GetService<JLUiCourseApi>();

                if (string.IsNullOrEmpty(QueryText))
                {
                    Courses = await api.QueryCoursesAsync(CurrentPage, 15);
                    ButtonEnabled = true;
                    return;
                }

                Courses = await api.QueryCoursesAsync(CurrentPage, 15, QueryText);
                ButtonEnabled = true;
            });
        }

        [RelayCommand]
        private static void AddToFavorites(Course course)
        {
            var api = App.ServiceProvider.GetService<JLUiCourseApi>();
            _ = api.AddToFavoritesAsync(course);
        }
    }
}
