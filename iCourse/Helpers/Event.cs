using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.Models;
using iCourse.ViewModels;
using iCourse.Views;

namespace iCourse.Helpers
{
    internal class Event
    {

        public void RegisterEvents()
        {
            WeakReferenceMessenger.Default.Register<ShowWindowMessage>(this, ShowWindow);
        }

        private void ShowWindow(object recipient, ShowWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(CaptchaWindowViewModel))
            {
                var captchaWindow = new CaptchaWindow(msg.Args[0] as string ?? String.Empty);
                captchaWindow.ShowDialog();
            }

            if (msg.ViewModelType == typeof(SelectBatchViewModel))
            {
                var batchInfos = msg.Args[0] as List<BatchInfo>;
                var selectBatchWindow = new SelectBatchWindow(batchInfos);
                selectBatchWindow.ShowDialog();
            }

        }
    }
}
