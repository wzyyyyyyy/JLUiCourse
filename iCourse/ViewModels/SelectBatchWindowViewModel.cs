using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iCourse.Models;
using iCourse.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace iCourse.ViewModels;

public partial class SelectBatchViewModel(
    IDialogService dialogs,
    IReadOnlyList<BatchInfo> batchList) : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<BatchInfo> batchList = batchList;

    [ObservableProperty]
    private BatchInfo? selectedBatch;

    [RelayCommand]
    private async Task ConfirmSelection(Window window)
    {
        if (SelectedBatch is null)
        {
            await dialogs.ShowMessageAsync("提示", "请选择一个批次");
            return;
        }

        window.Close(SelectedBatch);
    }
}
