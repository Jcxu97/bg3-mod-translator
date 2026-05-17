using System.Windows;
using System.Windows.Threading;
using BG3LocTool.Core;

namespace BG3LocTool.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    private static void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("UI", e.Exception);
        e.Handled = true;
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) Report("Domain", ex);
    }

    private static void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report("Task", e.Exception);
        e.SetObserved();
    }

    private static void Report(string source, Exception ex)
    {
        FileLogger.Append("Crash", $"[{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        try
        {
            MessageBox.Show(
                $"工具捕获到一个未处理异常,已写入日志(请发给开发者):\n\n" +
                $"%LocalAppData%\\BG3LocTool\\logs\\\n\n" +
                $"{ex.GetType().Name}: {ex.Message}",
                "BG3LocTool 出错", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
