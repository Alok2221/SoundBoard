using System.Windows;
using System.Windows.Threading;

namespace SoundboardApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        ShowError(args.Exception);
        args.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception ex)
            ShowError(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ShowError(args.Exception);
        args.SetObserved();
    }

    private static void ShowError(Exception ex)
    {
        try
        {
            var message = ex.GetBaseException().Message;
            MessageBox.Show(
                message,
                "Soundboard - error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
