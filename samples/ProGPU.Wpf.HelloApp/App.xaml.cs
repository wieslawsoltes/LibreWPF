using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ProGPU.Wpf.HelloApp;

public partial class App : Application
{
    internal static int StartupEventCount { get; private set; }

    internal static int StartupArgumentCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_HELLO_RUN_VALIDATE") == "1")
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
        }
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        StartupArgumentCount = e.Args.Length;
        Properties["HelloStartupArgumentCount"] = e.Args.Length;
        Properties["HelloStartupArguments"] = string.Join("|", e.Args);
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            var window = Current.MainWindow as MainWindow
                ?? Current.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? throw new InvalidOperationException("Expected HelloApp MainWindow.");

            HelloSelfTest.Validate(window, expectStartupActivation: true);
            Console.WriteLine("ProGPU WPF HelloApp Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }
}
