using CommunityToolkit.Mvvm.Messaging;
using iCourse.Messages;
using iCourse.ViewModels;
using iCourse.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iCourse.Helpers
{
    internal class Event
    {

        public void RegisterEvents()
        {
            WeakReferenceMessenger.Default.Register<ShowWindowMessage>(this, ShowCaptchaWindow);
        }

        private void ShowCaptchaWindow(object recipient, ShowWindowMessage msg)
        {
            if (msg.ViewModelType == typeof(CaptchaWindowViewModel))
            {
                var captchaWindow = new CaptchaWindow(msg.Args[0] as string ?? String.Empty);
                captchaWindow.Show();
            }
        }
    } 
}
