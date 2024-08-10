using System.IO;
using System.Windows;

namespace iCourse.Views
{
    public partial class DisclaimerWindow : Window
    {
        public bool IsAgreed { get; private set; }

        public DisclaimerWindow()
        {
            InitializeComponent();
            IsAgreed = false;
        }

        private void AgreeButton_Click(object sender, RoutedEventArgs e)
        {
            IsAgreed = true;
            if (NoShowCheckBox.IsChecked ?? false)
            {
                File.Create(".noshow");
            }
            this.Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
