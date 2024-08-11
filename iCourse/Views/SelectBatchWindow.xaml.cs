using CommunityToolkit.Mvvm.Messaging;
using iCourse.Helpers;
using iCourse.Models;
using iCourse.ViewModels;
using System.Windows;
using iCourse.Messages;

namespace iCourse.Views
{
    public partial class SelectBatchWindow : Window
    {
        public SelectBatchWindow(List<BatchInfo> batchList)
        {
            InitializeComponent();

            var viewModel = new SelectBatchViewModel(batchList);
            DataContext = viewModel;

            WeakReferenceMessenger.Default.Register<CloseWindowMessage>(this, CloseWindow);
        }

        private void CloseWindow(object recipient, CloseWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(SelectBatchViewModel))
            {
                this.Close();
            }
        }
    }
}