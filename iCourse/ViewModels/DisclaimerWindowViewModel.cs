using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using System.IO;
using System.Windows;

namespace iCourse.ViewModels
{
    partial class DisclaimerViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool isAgreed = false;

        [ObservableProperty]
        private bool noShowNextTime;

        [RelayCommand]
        private void Agree()
        {
            IsAgreed = true;

            if (NoShowNextTime)
            {
                File.Create(".noshow").Dispose();
            }

            WeakReferenceMessenger.Default.Send<CloseWindowMessage>(new CloseWindowMessage(typeof(DisclaimerViewModel)));
        }

        [RelayCommand]
        private void Decline()
        {
            Application.Current.Shutdown();
            throw new Exception("Disclaimer disagree.");
        }
    }
}