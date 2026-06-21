using System;
using System.IO;
using System.Windows;

namespace MultiRDPManager.FreeRDP;

public partial class App : System.Windows.Application
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "startup_log.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("App starting...");
        try
        {
            // Hook unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Log($"FATAL: {args.ExceptionObject}");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                Log($"Dispatcher exception: {args.Exception}");
                args.Handled = true;
            };

            base.OnStartup(e);
            Log("App started successfully");
        }
        catch (Exception ex)
        {
            Log($"Startup exception: {ex.GetType().Name}: {ex.Message}");
            Log($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"App exiting, code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
