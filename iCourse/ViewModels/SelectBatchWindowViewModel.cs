﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Messages;
using iCourse.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace iCourse.ViewModels
{
    partial class SelectBatchViewModel : ObservableObject
    {
        [ObservableProperty]
        private List<BatchInfo> batchList;

        [ObservableProperty]
        private BatchInfo selectedBatch;

        public SelectBatchViewModel()
        {
            // Provide some default value or handle initialization without parameters
            BatchList = [];
        }

        public SelectBatchViewModel(List<BatchInfo> batchList)
        {
            BatchList = batchList;
        }

        [RelayCommand]
        private void ConfirmSelection()
        {
            if (SelectedBatch == null)
            {
                MessageBox.Show("请选择一个批次");
                return;
            }

            App.ServiceProvider.GetService<UserCredentials>().LastBatchId = SelectedBatch.batchId;

            App.ServiceProvider.GetService<JLUiCourseApi>().SetBatchIdAsync(selectedBatch);

            WeakReferenceMessenger.Default.Send<CloseWindowMessage>(new CloseWindowMessage(typeof(SelectBatchViewModel)));
        }

        public static void ShowWindow(List<BatchInfo> batchInfos)
        {
            WeakReferenceMessenger.Default.Send<ShowWindowMessage>(new ShowWindowMessage(typeof(SelectBatchViewModel),
                batchInfos));
        }
    }
}