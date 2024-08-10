using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using System.Windows;
using iCourse.Messages;
using iCourse.ViewModels;

namespace iCourse.Views
{
    public partial class DisclaimerWindow : Window
    {
        public DisclaimerWindow()
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<CloseWindowMessage>(this, CloseWindow);
        }

        private void CloseWindow(object recipient, CloseWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(DisclaimerViewModel))
            {
                this.Close();
            }
        }
    }
}
