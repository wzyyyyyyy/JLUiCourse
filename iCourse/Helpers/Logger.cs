using System.Collections.ObjectModel;

namespace iCourse.Helpers
{
    public class Logger
    {
        public ObservableCollection<string> LogMessages { get; } = new();

        public void WriteLine<T>(T msg)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{time}] : {msg}";

            App.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(logMessage);
            });
        }
    }
}
