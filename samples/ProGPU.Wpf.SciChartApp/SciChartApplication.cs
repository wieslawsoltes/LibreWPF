#if PROGPU_WPF_REAL_SCICHART
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProGPU.DirectX;
using SciChart.Core;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting3D;
using SciChart.Charting3D.Axis;
using SciChart.Charting3D.Model;
using SciChart.Charting3D.RenderableSeries;

namespace ProGPU.Wpf.SciChartApp;

internal sealed record SciChartApplicationResult(
    int TwoDimensionalPointCount,
    int ThreeDimensionalPointCount,
    bool RenderedNativeBridgeSnapshot,
    int NativeBridgeDrawCount,
    string NativeDependencySummary,
    string NativeCompatibilitySummary,
    string NativeExportSummary,
    string NativeFacadeSummary,
    string NativeResolverSummary,
    FrameworkElement View,
    bool CreatedRealControls,
    string? NativeRuntimeFailure,
    RealSciChartLicenseStatus LicenseStatus);

internal sealed record RealSciChartLicenseStatus(
    bool Configured,
    string? EnvironmentVariable,
    string? Failure,
    string? NativeDependenciesPath,
    string? NativeDependenciesFailure);

internal sealed record RealSciChartNativeDependencyDiagnostics(
    string DependencySummary,
    string CompatibilitySummary,
    string ExportSummary,
    string FacadeSummary,
    IReadOnlyList<ProGpuDirectXNativeResolverRegistration> ResolverRegistrations)
{
    internal string ResolverSummary => ResolverRegistrations.Count == 0
        ? "none"
        : string.Join(" | ", ResolverRegistrations.Select(static registration => registration.Describe()));
}

internal static class SciChartApplication
{
    internal const string RuntimeLicenseEnvironmentVariable = "SCICHART_RUNTIME_LICENSE_KEY";
    internal const string LegacyRuntimeLicenseEnvironmentVariable = "PROGPU_WPF_SCICHART_LICENSE_KEY";
    internal const string NativeDependenciesPathEnvironmentVariable = "PROGPU_WPF_SCICHART_NATIVE_DEPENDENCIES_ROOT";

    private const int SampleCount = 96;
    private static readonly string[] KnownSciChartNativeModules =
    [
        "AbtLicensingNative.dll",
        "VXccelEngine3D.dll",
        "d3d9.dll",
        "d3d11.dll",
        "dxgi.dll",
        "D3DCOMPILER_47.dll"
    ];
    private static readonly Type[] SciChartAssemblyAnchorTypes =
    [
        typeof(NativeDllLoader),
        typeof(SciChart.Data.Model.DoubleRange),
        typeof(SciChart.Drawing.Common.IRenderSurface),
        typeof(SciChartSurface),
        typeof(SciChart3DSurface)
    ];
    private static readonly object NativeDiagnosticsGate = new();
    private static RealSciChartLicenseStatus? s_licenseStatus;
    private static RealSciChartNativeDependencyDiagnostics? s_nativeDiagnostics;

    internal static RealSciChartLicenseStatus ConfigureRuntimeLicenseFromEnvironment()
    {
        var nativeDependenciesPath = ConfigureNativeDependenciesPath();
        _ = EnsureNativeDiagnostics();
        if (s_licenseStatus is not null)
        {
            return s_licenseStatus;
        }

        var key = Environment.GetEnvironmentVariable(RuntimeLicenseEnvironmentVariable);
        var source = RuntimeLicenseEnvironmentVariable;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable(LegacyRuntimeLicenseEnvironmentVariable);
            source = LegacyRuntimeLicenseEnvironmentVariable;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            s_licenseStatus = new RealSciChartLicenseStatus(
                Configured: false,
                EnvironmentVariable: null,
                Failure: $"Missing {RuntimeLicenseEnvironmentVariable}.",
                nativeDependenciesPath.Path,
                nativeDependenciesPath.Failure);
            return s_licenseStatus;
        }

        try
        {
            SciChartSurface.SetRuntimeLicenseKey(key);
            s_licenseStatus = new RealSciChartLicenseStatus(
                Configured: true,
                EnvironmentVariable: source,
                Failure: null,
                nativeDependenciesPath.Path,
                nativeDependenciesPath.Failure);
        }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex))
        {
            s_licenseStatus = new RealSciChartLicenseStatus(
                Configured: false,
                EnvironmentVariable: source,
                Failure: ex.GetBaseException().Message,
                nativeDependenciesPath.Path,
                nativeDependenciesPath.Failure);
        }

        return s_licenseStatus;
    }

    internal static SciChartApplicationResult Create()
    {
        var licenseStatus = ConfigureRuntimeLicenseFromEnvironment();
        var nativeDiagnostics = EnsureNativeDiagnostics();
        var dataSeries2D = new XyDataSeries<double, double>();
        for (var i = 0; i < SampleCount; i++)
        {
            var x = i * 0.1;
            var y = Math.Sin(x) + Math.Cos(x * 0.4) * 0.35;
            dataSeries2D.Append(x, y);
        }

        var dataSeries3D = new XyzDataSeries3D<double>();
        var bridgePoints3D = SciChart3DBridgeSnapshotRenderer.CreateSamplePoints(SampleCount);
        foreach (var point in bridgePoints3D)
        {
            dataSeries3D.Append(point.X, point.Y, point.Z, null);
        }

        var bridgeSnapshot3D = SciChart3DBridgeSnapshotRenderer.Render(bridgePoints3D);
        SciChart3DBridgeSnapshotRenderer.Validate(bridgeSnapshot3D);

        if (!licenseStatus.Configured)
        {
            var fallback = CreateNativeBridgeFallbackView(
                $"Real SciChart packages restored and data-series APIs ran, but runtime license setup is unavailable: {licenseStatus.Failure}. Native dependency extraction: {DescribeNativeDependencyPath(licenseStatus)}. Native dependencies: {nativeDiagnostics.DependencySummary}. Native compatibility: {nativeDiagnostics.CompatibilitySummary}. Native exports: {nativeDiagnostics.ExportSummary}. Native facade: {nativeDiagnostics.FacadeSummary}. Native resolver: {nativeDiagnostics.ResolverSummary}.",
                bridgeSnapshot3D);

            return new SciChartApplicationResult(
                SampleCount,
                SampleCount,
                true,
                bridgeSnapshot3D.DrawCount,
                nativeDiagnostics.DependencySummary,
                nativeDiagnostics.CompatibilitySummary,
                nativeDiagnostics.ExportSummary,
                nativeDiagnostics.FacadeSummary,
                nativeDiagnostics.ResolverSummary,
                fallback,
                CreatedRealControls: false,
                NativeRuntimeFailure: licenseStatus.Failure,
                licenseStatus);
        }

        try
        {
            var lineSeries2D = new FastLineRenderableSeries
            {
                DataSeries = dataSeries2D
            };

            var surface2D = new SciChartSurface
            {
                MinHeight = 170,
                XAxis = new NumericAxis(),
                YAxis = new NumericAxis()
            };
            surface2D.RenderableSeries.Add(lineSeries2D);

            var lineSeries3D = new PointLineRenderableSeries3D
            {
                DataSeries = dataSeries3D,
                IsLineStrips = true
            };

            var surface3D = new SciChart3DSurface
            {
                MinHeight = 170,
                XAxis = new NumericAxis3D(),
                YAxis = new NumericAxis3D(),
                ZAxis = new NumericAxis3D()
            };
            surface3D.RenderableSeries.Add(lineSeries3D);

            var grid = CreateTwoColumnGrid(surface2D, surface3D);
            return new SciChartApplicationResult(
                SampleCount,
                SampleCount,
                true,
                bridgeSnapshot3D.DrawCount,
                nativeDiagnostics.DependencySummary,
                nativeDiagnostics.CompatibilitySummary,
                nativeDiagnostics.ExportSummary,
                nativeDiagnostics.FacadeSummary,
                nativeDiagnostics.ResolverSummary,
                grid,
                CreatedRealControls: true,
                NativeRuntimeFailure: null,
                licenseStatus);
        }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex))
        {
            var fallback = CreateNativeBridgeFallbackView(
                $"Real SciChart packages restored and data-series APIs ran, but native runtime is unavailable: {DescribeNativeRuntimeFailure(ex)}. Native dependency extraction: {DescribeNativeDependencyPath(licenseStatus)}. Native dependencies: {nativeDiagnostics.DependencySummary}. Native compatibility: {nativeDiagnostics.CompatibilitySummary}. Native exports: {nativeDiagnostics.ExportSummary}. Native facade: {nativeDiagnostics.FacadeSummary}. Native resolver: {nativeDiagnostics.ResolverSummary}.",
                bridgeSnapshot3D);

            return new SciChartApplicationResult(
                SampleCount,
                SampleCount,
                true,
                bridgeSnapshot3D.DrawCount,
                nativeDiagnostics.DependencySummary,
                nativeDiagnostics.CompatibilitySummary,
                nativeDiagnostics.ExportSummary,
                nativeDiagnostics.FacadeSummary,
                nativeDiagnostics.ResolverSummary,
                fallback,
                CreatedRealControls: false,
                NativeRuntimeFailure: ex.GetBaseException().Message,
                licenseStatus);
        }
    }

    internal static void Validate(SciChartApplicationResult result)
    {
        if (result.TwoDimensionalPointCount < SampleCount || result.ThreeDimensionalPointCount < SampleCount)
        {
            throw new InvalidOperationException("Expected real SciChart package data series to contain the Showcase sample points.");
        }

        if (!result.RenderedNativeBridgeSnapshot || result.NativeBridgeDrawCount < 2)
        {
            throw new InvalidOperationException("Expected real SciChart 3D package data to render through the ProGPU DirectX/WebGPU bridge.");
        }

        if (result.CreatedRealControls && result.View is not Grid { Children.Count: 2 })
        {
            throw new InvalidOperationException("Expected real SciChart package Showcase to create 2D and 3D chart controls.");
        }

        if (!result.LicenseStatus.Configured && string.IsNullOrWhiteSpace(result.LicenseStatus.Failure))
        {
            throw new InvalidOperationException("Expected unavailable real SciChart license setup to report the reason.");
        }

        if (string.IsNullOrWhiteSpace(result.LicenseStatus.NativeDependenciesPath)
            && string.IsNullOrWhiteSpace(result.LicenseStatus.NativeDependenciesFailure))
        {
            throw new InvalidOperationException("Expected real SciChart native dependency extraction path to be configured or report the failure.");
        }

        if (string.IsNullOrWhiteSpace(result.NativeDependencySummary))
        {
            throw new InvalidOperationException("Expected real SciChart native dependencies to be reported explicitly.");
        }

        if (string.IsNullOrWhiteSpace(result.NativeCompatibilitySummary))
        {
            throw new InvalidOperationException("Expected real SciChart native compatibility plan to be reported explicitly.");
        }

        if (string.IsNullOrWhiteSpace(result.NativeExportSummary))
        {
            throw new InvalidOperationException("Expected real SciChart native ABI export plan to be reported explicitly.");
        }

        if (string.IsNullOrWhiteSpace(result.NativeFacadeSummary))
        {
            throw new InvalidOperationException("Expected real SciChart native facade source plan to be reported explicitly.");
        }

        if (string.IsNullOrWhiteSpace(result.NativeResolverSummary))
        {
            throw new InvalidOperationException("Expected real SciChart native resolver state to be reported explicitly.");
        }

        if (!result.CreatedRealControls && string.IsNullOrWhiteSpace(result.NativeRuntimeFailure))
        {
            throw new InvalidOperationException("Expected real SciChart native-runtime failures to be reported explicitly.");
        }
    }

    private static RealSciChartNativeDependencyDiagnostics EnsureNativeDiagnostics()
    {
        lock (NativeDiagnosticsGate)
        {
            if (s_nativeDiagnostics is not null)
            {
                return s_nativeDiagnostics;
            }

            var moduleHints = CreateNativeModuleHints();
            var report = ProGpuDirectXNativeDependencyInspector.Inspect(
                Array.Empty<ProGpuDirectXNativeImport>(),
                moduleHints);
            var plan = ProGpuDirectXNativeCompatibilityPlanner.Create(report);
            var abiPlan = ProGpuDirectXNativeAbiPlanner.Create(report);
            var facadeSource = ProGpuDirectXNativeFacadeSourceEmitter.Emit(abiPlan);
            var resolverOptions = ProGpuDirectXNativeResolverOptions.FromEnvironment();
            var registrations = ProGpuDirectXNativeResolver.RegisterAnchorTypes(
                SciChartAssemblyAnchorTypes,
                plan,
                resolverOptions);

            s_nativeDiagnostics = new RealSciChartNativeDependencyDiagnostics(
                report.DescribeModules(),
                plan.DescribeRequiredActions(),
                abiPlan.DescribeActionableExports(maxExportsPerModule: 8),
                facadeSource.DescribeSupport(),
                registrations);
            return s_nativeDiagnostics;
        }
    }

    private static IReadOnlyList<ProGpuDirectXNativeModuleHint> CreateNativeModuleHints()
    {
        var moduleHints = new List<ProGpuDirectXNativeModuleHint>(
            ProGpuDirectXNativeDependencyInspector.CreateModuleHintsFromAssemblyImages(
                SciChartAssemblyAnchorTypes,
                "SciChartAssemblyImage"));

        foreach (var moduleName in KnownSciChartNativeModules)
        {
            moduleHints.Add(new ProGpuDirectXNativeModuleHint(
                "SciChart.Native",
                moduleName,
                "KnownSciChartRuntime"));
        }

        return moduleHints;
    }

    private static (string? Path, string? Failure) ConfigureNativeDependenciesPath()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable(NativeDependenciesPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(Path.GetTempPath(), "ProGPU.Wpf.SciChartApp", "SciChartDependencies");
            }

            Directory.CreateDirectory(path);

            NativeDllLoader.DependenciesPathRoot = path;
            return (path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TypeInitializationException)
        {
            return (null, ex.GetBaseException().Message);
        }
    }

    private static string DescribeNativeDependencyPath(RealSciChartLicenseStatus licenseStatus)
    {
        if (!string.IsNullOrWhiteSpace(licenseStatus.NativeDependenciesPath))
        {
            return licenseStatus.NativeDependenciesFailure is null
                ? licenseStatus.NativeDependenciesPath
                : $"{licenseStatus.NativeDependenciesPath} ({licenseStatus.NativeDependenciesFailure})";
        }

        return licenseStatus.NativeDependenciesFailure ?? "unavailable";
    }

    private static string DescribeNativeRuntimeFailure(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message) ? "native runtime failure" : message;
    }

    private static Grid CreateTwoColumnGrid(FrameworkElement surface2D, FrameworkElement surface3D)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        surface2D.Margin = new Thickness(0, 0, 8, 0);
        surface3D.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(surface3D, 1);
        grid.Children.Add(surface2D);
        grid.Children.Add(surface3D);
        return grid;
    }

    private static FrameworkElement CreateNativeBridgeFallbackView(string message, SciChart3DBridgeSnapshot bridgeSnapshot)
    {
        var stackPanel = new StackPanel
        {
            Margin = new Thickness(8)
        };
        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stackPanel.Children.Add(new Image
        {
            Source = SciChart3DBridgeSnapshotRenderer.CreateBitmap(bridgeSnapshot),
            Width = bridgeSnapshot.Width,
            Height = bridgeSnapshot.Height,
            Stretch = Stretch.None
        });
        return stackPanel;
    }

    private static bool IsNativeRuntimeFailure(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is DllNotFoundException or TypeInitializationException)
            {
                return true;
            }

            if (current.Message.Contains("AbtLicensingNative", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
