using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ProGPU.Wpf.SciChartApp;

public partial class App : Application
{
    internal static int StartupEventCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
#if PROGPU_WPF_REAL_SCICHART
        SciChartApplication.ConfigureRuntimeLicenseFromEnvironment();
#endif

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SCICHART_VALIDATE") == "1")
        {
            SciChartShowcaseSelfTest.Validate(SciChartRenderer.Render());
#if PROGPU_WPF_REAL_SCICHART
            var realSciChartResult = SciChartApplication.Create();
            SciChartApplication.Validate(realSciChartResult);
            if (realSciChartResult.CreatedRealControls)
            {
                Console.WriteLine("ProGPU WPF real SciChart package Showcase validation succeeded.");
            }
            else
            {
                var nativeExtraction = realSciChartResult.LicenseStatus.NativeDependenciesPath
                    ?? realSciChartResult.LicenseStatus.NativeDependenciesFailure
                    ?? "unavailable";
                Console.WriteLine($"ProGPU WPF real SciChart package Showcase restored and validated data APIs; native runtime unavailable. Native extraction: {nativeExtraction}. Native dependencies: {realSciChartResult.NativeDependencySummary}. Native compatibility: {realSciChartResult.NativeCompatibilitySummary}. Native exports: {realSciChartResult.NativeExportSummary}. Native facade: {realSciChartResult.NativeFacadeSummary}. Native resolver: {realSciChartResult.NativeResolverSummary}.");
            }
#endif
            Shutdown();
            Console.WriteLine("ProGPU WPF SciChart Showcase validation succeeded.");
            return;
        }

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SCICHART_RUN_VALIDATE") == "1")
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
        Properties["SciChartShowcaseStartupArgumentCount"] = e.Args.Length;
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
                ?? throw new InvalidOperationException("Expected SciChart Showcase StartupUri MainWindow.");

            window.ValidateRenderedChart();
            Console.WriteLine("ProGPU WPF SciChart Showcase Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }
}
