using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.ViewModels;

namespace iCourse.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            ShowDisclaimerWindow();
            InitializeComponent();
        }

        private void ShowDisclaimerWindow()
        {
            if (File.Exists(".noshow"))
            {
                return;
            }

            var disclaimerWindow = new DisclaimerWindow();
            disclaimerWindow.ShowDialog();

        }
    }
}