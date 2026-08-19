#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.42}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.51}"
bundle_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE:-${package_output}/librewpf-preview-${dev_package_version}.tar.gz}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

require_package_cache_entry() {
  local package_id="$1"
  local package_version
  package_version="$(progpu_preview_package_version "${package_id}")"
  local package_key
  package_key="$(printf '%s' "${package_id}" | tr '[:upper:]' '[:lower:]')"
  local package_dir="${smoke_root}/packages/${package_key}/${package_version}"
  if [[ ! -f "${package_dir}/${package_key}.${package_version}.nupkg" ]]; then
    echo "Expected restored package ${package_id} ${package_version} in ${package_dir}." >&2
    exit 1
  fi
}

"${repo_root}/eng/progpu-preview-release-verify.sh"

if [[ -n "${PROGPU_WPF_PREVIEW_RELEASE_SDK_SMOKE_ROOT:-}" ]]; then
  smoke_root="${PROGPU_WPF_PREVIEW_RELEASE_SDK_SMOKE_ROOT}"
  rm -rf "${smoke_root}"
  mkdir -p "${smoke_root}"
else
  smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/progpu-wpf-preview-sdk-smoke.XXXXXX")"
  trap 'rm -rf "${smoke_root}"' EXIT
fi

feed_dir="${smoke_root}/feed"
mkdir -p "${feed_dir}"
tar -xzf "${bundle_output}" -C "${feed_dir}"
project_dir="${feed_dir}/BundleSdkSmoke"
mkdir -p "${project_dir}"
sdk_sample_target_framework="${PROGPU_WPF_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0-windows}"

cat >"${project_dir}/BundleSdkSmoke.csproj" <<PROJECT
<Project Sdk="LibreWPF.Sdk/${dev_package_version}">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>${sdk_sample_target_framework}</TargetFramework>
    <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
    <UseWPF>true</UseWPF>
    <AssemblyName>BundleSdkSmoke</AssemblyName>
    <RootNamespace>BundleSdkSmoke</RootNamespace>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
PROJECT

cat >"${project_dir}/App.xaml" <<'XAML'
<Application
    x:Class="BundleSdkSmoke.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=System.Runtime">
    <Application.Resources>
        <sys:String x:Key="BundleSmokeText">Preview bundle SDK smoke</sys:String>
        <SolidColorBrush x:Key="BundleSmokeBrush" Color="#2B6CB0" />
    </Application.Resources>
</Application>
XAML

cat >"${project_dir}/App.xaml.cs" <<'CS'
using System;
using System.Windows;
using System.Windows.Media;

namespace BundleSdkSmoke;

public partial class App : Application
{
    private const string ExpectedText = "Preview bundle SDK smoke";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (string.Equals(Environment.GetEnvironmentVariable("PROGPU_WPF_BUNDLE_SDK_SMOKE_VALIDATE"), "1", StringComparison.Ordinal))
        {
            MainWindow window = new();
            if (!string.Equals(window.Message.Text, ExpectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected TextBlock text '{ExpectedText}', found '{window.Message.Text}'.");
            }

            if (!string.Equals(window.ActionButton.Content as string, ExpectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ElementName binding did not update the button content.");
            }

            if (FindResource("BundleSmokeBrush") is not SolidColorBrush brush || brush.Color.R != 0x2B || brush.Color.G != 0x6C || brush.Color.B != 0xB0)
            {
                throw new InvalidOperationException("Application resource lookup did not return the expected brush.");
            }

            Console.WriteLine("LibreWPF preview release bundle SDK smoke succeeded.");
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}
CS

cat >"${project_dir}/MainWindow.xaml" <<'XAML'
<Window
    x:Class="BundleSdkSmoke.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="LibreWPF Preview Bundle SDK Smoke"
    Width="360"
    Height="220">
    <StackPanel x:Name="RootPanel" Margin="16">
        <TextBlock
            x:Name="Message"
            Foreground="{StaticResource BundleSmokeBrush}"
            Text="{DynamicResource BundleSmokeText}" />
        <Button
            x:Name="ActionButton"
            Margin="0,12,0,0"
            Content="{Binding ElementName=Message, Path=Text}" />
    </StackPanel>
</Window>
XAML

cat >"${project_dir}/MainWindow.xaml.cs" <<'CS'
using System.Windows;

namespace BundleSdkSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
CS

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" build "${project_dir}/BundleSdkSmoke.csproj" -v:minimal

require_package_cache_entry "LibreWPF.Transport"
require_package_cache_entry "LibreWPF.ProGPU"
require_package_cache_entry "LibreWPF.Sdk"

NUGET_PACKAGES="${smoke_root}/packages" \
PROGPU_WPF_BUNDLE_SDK_SMOKE_VALIDATE=1 \
  "${dotnet}" run --project "${project_dir}/BundleSdkSmoke.csproj" --no-build -v:minimal

avalonia_project_dir="${feed_dir}/BundleAvaloniaSmoke"
mkdir -p "${avalonia_project_dir}"

cat >"${avalonia_project_dir}/Directory.Packages.props" <<PROJECT
<Project>
  <Import Project="${repo_root}/external/ProGPU/Directory.Packages.props" />
  <ItemGroup>
    <PackageVersion Include="ProGPU.Avalonia" Version="${progpu_package_version}" />
  </ItemGroup>
</Project>
PROJECT

cat >"${avalonia_project_dir}/BundleAvaloniaSmoke.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="ProGPU.Avalonia" />
  </ItemGroup>
</Project>
PROJECT

cat >"${avalonia_project_dir}/App.axaml" <<'XAML'
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="BundleAvaloniaSmoke.App">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
XAML

cat >"${avalonia_project_dir}/App.axaml.cs" <<'CS'
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BundleAvaloniaSmoke;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
CS

cat >"${avalonia_project_dir}/MainWindow.axaml" <<'XAML'
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:progpu="clr-namespace:ProGPU.Avalonia;assembly=ProGPU.Avalonia"
        x:Class="BundleAvaloniaSmoke.MainWindow"
        Title="ProGPU Avalonia Preview Bundle Smoke"
        Width="640"
        Height="360"
        Background="#111318">
  <Grid RowDefinitions="Auto,*">
    <TextBlock Margin="12"
               Text="ProGPU.Avalonia preview bundle smoke"
               Foreground="White" />
    <progpu:ProGpuHostControl x:Name="Host"
                              Grid.Row="1"
                              CornerRadius="4"
                              EnableSharedTextureMemory="False"
                              EnableSharedImageReadback="False" />
  </Grid>
</Window>
XAML

cat >"${avalonia_project_dir}/MainWindow.axaml.cs" <<'CS'
using Avalonia.Controls;
using WinuiGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace BundleAvaloniaSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Host.WinuiRoot = new WinuiGrid
        {
            Width = 64,
            Height = 64
        };
        Loaded += (_, _) => Host.RequestRender();
    }
}
CS

cat >"${avalonia_project_dir}/Program.cs" <<'CS'
using System;
using Avalonia;

namespace BundleAvaloniaSmoke;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
CS

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" build "${avalonia_project_dir}/BundleAvaloniaSmoke.csproj" -v:minimal /p:UseSharedCompilation=false

require_package_cache_entry "ProGPU.Avalonia"
require_package_cache_entry "ProGPU.WinUI"

echo "ProGPU Avalonia preview release bundle package smoke succeeded."
