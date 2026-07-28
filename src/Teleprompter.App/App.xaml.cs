using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Teleprompter.App;

/// <summary>Application entry point with last-resort crash logging.</summary>
public partial class App : Application
{
    private static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TalkPrompter",
        "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the saved theme before any window is created so the first
        // frame already has the right palette.
        Services.ThemeService.Apply(
            Services.ThemeService.Parse(Services.JsonSettingsStore.Load().Theme));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception, "appdomain");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception, "task");
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = WriteCrashLog(e.Exception, "ui");
        MessageBox.Show(
            "Something went wrong and the app has to close.\n\n" +
            $"{e.Exception.Message}\n\nDetails were written to:\n{logPath}",
            "TalkPrompter",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        // Let the app terminate; state after an unhandled UI exception is not trustworthy.
    }

    private static string WriteCrashLog(Exception? exception, string origin)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{origin}.txt");
            File.WriteAllText(path, exception?.ToString() ?? "Unknown error (no exception object).");
            return path;
        }
        catch (Exception)
        {
            return "(log could not be written)";
        }
    }
}
