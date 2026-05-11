using System.Windows;
using System.Windows.Threading;

namespace CDriveMigrator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handler (UI thread)
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"发生未处理的异常:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "CDriveMigrator 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Unobserved task exceptions (background threads)
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            args.SetObserved();
            Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    $"后台任务异常:\n\n{args.Exception?.InnerException?.Message ?? args.Exception?.Message}",
                    "CDriveMigrator 错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        };

        // AppDomain unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"致命异常:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "CDriveMigrator 致命错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
    }
}
