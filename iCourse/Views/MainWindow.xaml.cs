using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.ViewModels;

namespace iCourse.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            ShowDisclaimer();
            InitializeComponent();
        }

        private void ShowDisclaimer()
        {
            if (File.Exists(".noshow"))
            {
                return;
            }

            var disclaimerWindow = new DisclaimerWindow();
            disclaimerWindow.ShowDialog();

            if (!disclaimerWindow.IsAgreed)
            {
                throw new Exception("Disclaimer not agreed.");
            }
        }
    }
}