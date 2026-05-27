using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Models;
using iCourse.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.ViewModels;

public partial class QueryCourseWindowViewModel(IJLUiCourseApi api) : ObservableObject
{
    [ObservableProperty]
    private List<Course> courses = [];

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private bool buttonEnabled;

    [ObservableProperty]
    private int currentPage = 1;

    public async Task InitializeAsync()
    {
        Courses = await api.QueryCoursesAsync(CurrentPage, 15);
        ButtonEnabled = true;
    }

    [RelayCommand]
    private async Task PreviousPage()
    {
        if (CurrentPage == 1)
        {
            return;
        }

        CurrentPage--;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPage()
    {
        CurrentPage++;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task Query()
    {
        CurrentPage = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private void AddToFavorites(Course course)
    {
        _ = api.AddToFavoritesAsync(course);
    }

    private async Task LoadPageAsync()
    {
        ButtonEnabled = false;
        Courses = string.IsNullOrWhiteSpace(QueryText)
            ? await api.QueryCoursesAsync(CurrentPage, 15)
            : await api.QueryCoursesAsync(CurrentPage, 15, QueryText);
        ButtonEnabled = true;
    }
}
