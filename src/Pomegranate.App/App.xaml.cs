using System.Windows;
using System.Windows.Threading;
using Pomegranate.App.Infrastructure;

namespace Pomegranate.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var command = AppCommand.FromArgs(e.Args);

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            // Already running: hand over what this launch was for, then leave.
            if (command is not null)
            {
                SingleInstance.TrySend(command);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        Log.CleanUp();
        Log.Info($"Pomegranate {AppInfo.Version} starting (packaged: {AppInfo.IsPackaged}). Data folder: {AppPaths.DataFolder}");

        _controller = new AppController(this, _instance);
        _controller.Start(command);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_instance is not null)
        {
            _instance.Dispose();
            Log.Info("Pomegranate stopped.");
        }

        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray app shouldn't vanish over one bad event; log it and keep running.
        Log.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
    }
}
