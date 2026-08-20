param(
    [string] $PackageDirectory = "",
    [string] $Version = "",
    [string] $RuntimeIdentifier = "",
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $BuildOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot "artifacts/packages/Release/NonShipping"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $env:PROGPU_WPF_DEV_PACKAGE_VERSION
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.1.0-preview.43"
}

$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$sdkPackage = Join-Path $PackageDirectory "LibreWPF.Sdk.$Version.nupkg"
$transportPackage = Join-Path $PackageDirectory "LibreWPF.Transport.$Version.nupkg"
if (!(Test-Path $sdkPackage) -or !(Test-Path $transportPackage)) {
    throw "LibreWPF AnyCPU smoke requires $sdkPackage and $transportPackage."
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-windows-anycpu-$([guid]::NewGuid().ToString('N'))"
$projectRoot = Join-Path $smokeRoot "App"
$packagesRoot = Join-Path $smokeRoot "packages"
New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null

try {
    $runtimeIdentifierProperty = ""
    if (![string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $runtimeIdentifierProperty = "    <RuntimeIdentifier>$RuntimeIdentifier</RuntimeIdentifier>"
    }

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="librewpf-local" value="$PackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="dotnet11" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json" />
    <add key="dotnet11-transport" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11-transport/nuget/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $smokeRoot "NuGet.config") -Encoding utf8

    @"
<Project Sdk="LibreWPF.Sdk/$Version">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
$runtimeIdentifierProperty
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $projectRoot "AnyCpuSmoke.csproj") -Encoding utf8

    @'
<Application x:Class="AnyCpuSmoke.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
'@ | Set-Content -Path (Join-Path $projectRoot "App.xaml") -Encoding utf8

    @'
using System.Windows;

namespace AnyCpuSmoke;

public partial class App : Application
{
}
'@ | Set-Content -Path (Join-Path $projectRoot "App.xaml.cs") -Encoding utf8

    @'
<Window x:Class="AnyCpuSmoke.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LibreWPF AnyCPU Smoke"
        Width="320"
        Height="180">
  <TextBlock x:Name="SmokeText"
             Text="LibreWPF visible text"
             FontSize="32"
             Foreground="Black" />
</Window>
'@ | Set-Content -Path (Join-Path $projectRoot "MainWindow.xaml") -Encoding utf8

    @'
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnyCpuSmoke;

public partial class MainWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _renderLifetimeTimer;
    private string? _nativePath;

    public MainWindow()
    {
        InitializeComponent();
        _renderLifetimeTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _renderLifetimeTimer.Tick += OnRenderLifetimeElapsed;
        ContentRendered += OnContentRendered;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;

        string nativePath = Path.Combine(AppContext.BaseDirectory, "PresentationNative_cor3.dll");
        if (!File.Exists(nativePath))
        {
            throw new FileNotFoundException("LibreWPF did not select the native WPF runtime for the current AnyCPU process.", nativePath);
        }

        _nativePath = nativePath;
        AssertTextRendered();
        AssertClipboardRoundTrip();
        _renderLifetimeTimer.Start();
    }

    private void AssertTextRendered()
    {
        SmokeText.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(SmokeText.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(SmokeText.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(SmokeText);

        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        bitmap.CopyPixels(pixels, stride, 0);

        int coveredPixels = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                coveredPixels++;
            }
        }

        if (coveredPixels < 32)
        {
            throw new InvalidOperationException(
                $"LibreWPF rendered no visible text pixels (coverage={coveredPixels}).");
        }

        Console.WriteLine($"LibreWPF visible-text smoke rendered {coveredPixels} covered pixels.");
    }

    private static void AssertClipboardRoundTrip()
    {
        const string expected = "LibreWPF Windows clipboard smoke";
        Clipboard.SetText(expected);
        string actual = Clipboard.GetText();
        if (!Clipboard.ContainsText() || !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"LibreWPF Windows clipboard round-trip failed (actual='{actual}').");
        }

        Clipboard.Clear();
        Console.WriteLine("LibreWPF Windows clipboard round-trip succeeded.");
    }

    private void OnRenderLifetimeElapsed(object? sender, EventArgs e)
    {
        _renderLifetimeTimer.Stop();
        string nativePath = _nativePath ?? throw new InvalidOperationException("The rendered native runtime path was not captured.");
        Console.WriteLine($"LibreWPF Windows AnyCPU smoke succeeded with {nativePath}.");
        Application.Current.Shutdown(0);
    }
}
'@ | Set-Content -Path (Join-Path $projectRoot "MainWindow.xaml.cs") -Encoding utf8

    $oldPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packagesRoot
    try {
        dotnet restore (Join-Path $projectRoot "AnyCpuSmoke.csproj") --configfile (Join-Path $smokeRoot "NuGet.config") --force --no-cache
        if ($LASTEXITCODE -ne 0) { throw "LibreWPF Windows AnyCPU restore failed." }

        dotnet build (Join-Path $projectRoot "AnyCpuSmoke.csproj") --no-restore -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "LibreWPF Windows AnyCPU build failed." }

        $configurationOutput = Join-Path $projectRoot "bin/$Configuration"
        $nativeAsset = Get-ChildItem -Path $configurationOutput -Filter "PresentationNative_cor3.dll" -Recurse | Select-Object -First 1
        if ($null -eq $nativeAsset) {
            throw "LibreWPF Windows AnyCPU build output is missing PresentationNative_cor3.dll."
        }

        $managedFrameworkAsset = Get-ChildItem -Path $configurationOutput -Filter "PresentationFramework.dll" -Recurse | Select-Object -First 1
        if ($null -eq $managedFrameworkAsset) {
            throw "LibreWPF Windows AnyCPU build output is missing PresentationFramework.dll."
        }

        $directWriteForwarderAsset = Get-ChildItem -Path $configurationOutput -Filter "DirectWriteForwarder.dll" -Recurse | Select-Object -First 1
        if ($null -eq $directWriteForwarderAsset) {
            throw "LibreWPF Windows AnyCPU build output is missing DirectWriteForwarder.dll."
        }

        $ijwHostAsset = Get-ChildItem -Path $configurationOutput -Filter "ijwhost.dll" -Recurse | Select-Object -First 1
        if ($null -eq $ijwHostAsset) {
            throw "LibreWPF Windows AnyCPU build output is missing ijwhost.dll."
        }

        $dependencyFile = Get-ChildItem -Path $configurationOutput -Filter "AnyCpuSmoke.deps.json" -Recurse | Select-Object -First 1
        if ($null -eq $dependencyFile) {
            throw "LibreWPF Windows AnyCPU build output is missing AnyCpuSmoke.deps.json."
        }

        $dependencyText = Get-Content $dependencyFile.FullName -Raw
        if (!$dependencyText.Contains('lib/net10.0/PresentationFramework.dll', [StringComparison]::Ordinal)) {
            throw "LibreWPF Windows AnyCPU dependency file does not contain PresentationFramework.dll."
        }

        if (!$dependencyText.Contains('DirectWriteForwarder.dll', [StringComparison]::Ordinal)) {
            throw "LibreWPF Windows AnyCPU dependency file does not contain DirectWriteForwarder.dll."
        }

        if (!$dependencyText.Contains('ijwhost.dll', [StringComparison]::Ordinal)) {
            throw "LibreWPF Windows AnyCPU dependency file does not contain ijwhost.dll."
        }

        if (!$BuildOnly) {
            $appHost = Get-ChildItem -Path $configurationOutput -Filter "AnyCpuSmoke.exe" -Recurse | Select-Object -First 1
            if ($null -eq $appHost) {
                throw "LibreWPF Windows AnyCPU build output is missing the AnyCPU app host."
            }

            $stdoutPath = Join-Path $smokeRoot "$Configuration-stdout.log"
            $stderrPath = Join-Path $smokeRoot "$Configuration-stderr.log"
            $appProcess = Start-Process `
                -FilePath $appHost.FullName `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath `
                -PassThru
            if (!$appProcess.WaitForExit(30000)) {
                Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue
                throw "LibreWPF Windows AnyCPU launch timed out."
            }

            if ($appProcess.ExitCode -ne 0) {
                if (Test-Path $stdoutPath) {
                    Write-Host "LibreWPF Windows AnyCPU stdout:"
                    Get-Content $stdoutPath | Write-Host
                }
                if (Test-Path $stderrPath) {
                    Write-Host "LibreWPF Windows AnyCPU stderr:"
                    Get-Content $stderrPath | Write-Host
                }
                throw "LibreWPF Windows AnyCPU launch failed with exit code $($appProcess.ExitCode)."
            }
        }
    }
    finally {
        $env:NUGET_PACKAGES = $oldPackages
    }
}
finally {
    Remove-Item -Path $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
}
