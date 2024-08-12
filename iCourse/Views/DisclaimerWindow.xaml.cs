using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.ViewModels;
using System.Windows;

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
