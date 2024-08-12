using System.IO;
using System.Windows;

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