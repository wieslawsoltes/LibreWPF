using System;
using System.Windows;
using System.Windows.Threading;

namespace ProGPU.Wpf.ToolkitApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("PROGPU_WPF_TOOLKIT_VALIDATE") == "1")
        {
            var window = new MainWindow();
            ToolkitSelfTest.Validate(window);
            Shutdown();
            Console.WriteLine("ProGPU WPF Toolkit validation succeeded.");
            return;
        }

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_TOOLKIT_RUN_VALIDATE") == "1")
        {
            base.OnStartup(e);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
            return;
        }

        base.OnStartup(e);
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            var window = Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException("Expected Toolkit StartupUri MainWindow.");
            ToolkitSelfTest.Validate(window, expectLoaded: true);
            Console.WriteLine("ProGPU WPF Toolkit Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }
}
