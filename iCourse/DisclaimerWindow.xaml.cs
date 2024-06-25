using System.Windows;

namespace iCourse
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
            this.Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
