using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class App : Application
{
    internal static int StartupEventCount { get; private set; }

    internal static int StartupArgumentCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    internal static int LastExitCode { get; private set; } = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SHOWCASE_VALIDATE") == "1")
        {
            var window = new MainWindow();
            ShowcaseSelfTest.Validate(window);
            Shutdown();
            Console.WriteLine("ProGPU WPF Showcase validation succeeded.");
            return;
        }

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SHOWCASE_RUN_VALIDATE") == "1")
        {
            base.OnStartup(e);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
            return;
        }

        base.OnStartup(e);
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        StartupArgumentCount = e.Args.Length;
        Properties["ShowcaseStartupProperty"] = "Startup property ready";
        Properties["ShowcaseStartupArgumentCount"] = e.Args.Length;
        Resources["ShowcaseStartupText"] = "Startup resource ready";
        Resources["ShowcaseStartupBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x5A, 0x64));
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
        LastExitCode = e.ApplicationExitCode;
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            var window = FindMainWindow()
                ?? throw new InvalidOperationException("Expected Showcase StartupUri MainWindow.");
            ShowcaseSelfTest.Validate(window, expectLoadedStoryboardApplied: true);
            Console.WriteLine("ProGPU WPF Showcase Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }

    private static MainWindow? FindMainWindow()
    {
        var application = Current;
        if (application?.MainWindow is MainWindow mainWindow)
        {
            return mainWindow;
        }

        if (application == null)
        {
            return null;
        }

        foreach (Window window in application.Windows)
        {
            if (window is MainWindow candidate)
            {
                return candidate;
            }
        }

        return null;
    }
}
