using iCourse.Helpers;
using iCourse.Models;
using iCourse.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace iCourse
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 捕获UI线程未处理的异常
            Application.Current.DispatcherUnhandledException += DispatcherUnhandledExceptionHandler;

            // 捕获非UI线程未处理的异常
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            var _event = ServiceProvider.GetService<Event>();
            _event.RegisterEvents();

            var credentials = ServiceProvider.GetService<UserCredentials>();
            credentials.Load();

            var mainWindow = ServiceProvider.GetService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<Logger>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<Event>();
            services.AddSingleton<JLUiCourseApi>();
            services.AddSingleton<UserCredentials>();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // 清理事件处理程序
            Application.Current.DispatcherUnhandledException -= DispatcherUnhandledExceptionHandler;
            AppDomain.CurrentDomain.UnhandledException -= UnhandledExceptionHandler;
        }

        private void DispatcherUnhandledExceptionHandler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 记录异常
            LogException(e.Exception);
            e.Handled = true; // 防止应用程序退出
        }

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            // 记录异常
            if (e.ExceptionObject is Exception exception)
            {
                LogException(exception);
            }
        }

        private void LogException(Exception exception)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logFileName = $"error_{timestamp}.log";
            var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);
            var errorMessage = $"[{DateTime.Now}] {exception.ToString()}\n";

            // 写入日志文件
            File.AppendAllText(logFilePath, errorMessage);
        }
    }
}
