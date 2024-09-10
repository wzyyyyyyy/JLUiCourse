using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.ViewModels;
using System.Windows;

namespace iCourse.Views
{
    /// <summary>
    /// QueryCourseWindow.xaml 的交互逻辑
    /// </summary>
    public partial class QueryCourseWindow : Window
    {
        public QueryCourseWindow()
        {
            InitializeComponent();

            WeakReferenceMessenger.Default.Register<CloseWindowMessage>(this, CloseWindow);
        }

        private void CloseWindow(object recipient, CloseWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(QueryCourseWindowViewModel))
            {
                this.Close();
            }
        }
    }
}
