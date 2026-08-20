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
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.54}"
smoke_root="${repo_root}/artifacts/nuget/ProGPU.Avalonia.PackageSmoke"
project_dir="${smoke_root}/src"

pack_project() {
  local project="$1"
  local package_id="$2"
  rm -f \
    "${package_output}/${package_id}.${progpu_package_version}.nupkg" \
    "${package_output}/${package_id}.${progpu_package_version}.snupkg"
  "${dotnet}" pack "${repo_root}/${project}" -c Release -o "${package_output}" -v:minimal
}

required_package_exists() {
  local package_id="$1"
  [[ -f "${package_output}/${package_id}.${progpu_package_version}.nupkg" ]]
}

ensure_packages() {
  if [[ "${PROGPU_AVALONIA_PACKAGE_SMOKE_REBUILD_PACKAGES:-0}" != "1" ]] &&
     required_package_exists "ProGPU.Avalonia" &&
     required_package_exists "ProGPU.Backend.Dawn" &&
     required_package_exists "ProGPU.WinRT" &&
     required_package_exists "ProGPU.Media" &&
     required_package_exists "ProGPU.Media.Scene" &&
     required_package_exists "ProGPU.WinUI" &&
     required_package_exists "ProGPU.Virtualization" &&
     required_package_exists "ProGPU.Layout" &&
     required_package_exists "ProGPU.Scene" &&
     required_package_exists "ProGPU.Text" &&
     required_package_exists "ProGPU.Vector" &&
     required_package_exists "ProGPU.Compute" &&
     required_package_exists "ProGPU.Transpiler" &&
     required_package_exists "ProGPU.Backend"; then
    return
  fi

  mkdir -p "${package_output}"
  echo "Packing ProGPU Avalonia package smoke dependencies..."
  pack_project "external/ProGPU/src/ProGPU.Backend/ProGPU.Backend.csproj" "ProGPU.Backend"
  pack_project "external/ProGPU/src/ProGPU.Backend.Dawn/ProGPU.Backend.Dawn.csproj" "ProGPU.Backend.Dawn"
  pack_project "external/ProGPU/src/ProGPU.Transpiler/ProGPU.Transpiler.csproj" "ProGPU.Transpiler"
  pack_project "external/ProGPU/src/ProGPU.Compute/ProGPU.Compute.csproj" "ProGPU.Compute"
  pack_project "external/ProGPU/src/ProGPU.Vector/ProGPU.Vector.csproj" "ProGPU.Vector"
  pack_project "external/ProGPU/src/ProGPU.Text/ProGPU.Text.csproj" "ProGPU.Text"
  pack_project "external/ProGPU/src/ProGPU.Scene/ProGPU.Scene.csproj" "ProGPU.Scene"
  pack_project "external/ProGPU/src/ProGPU.Layout/ProGPU.Layout.csproj" "ProGPU.Layout"
  pack_project "external/ProGPU/src/ProGPU.Virtualization/ProGPU.Virtualization.csproj" "ProGPU.Virtualization"
  pack_project "external/ProGPU/src/ProGPU.WinRT/ProGPU.WinRT.csproj" "ProGPU.WinRT"
  pack_project "external/ProGPU/src/ProGPU.Media/ProGPU.Media.csproj" "ProGPU.Media"
  pack_project "external/ProGPU/src/ProGPU.Media.Scene/ProGPU.Media.Scene.csproj" "ProGPU.Media.Scene"
  pack_project "external/ProGPU/src/ProGPU.WinUI/ProGPU.WinUI.csproj" "ProGPU.WinUI"
  pack_project "external/ProGPU/src/ProGPU.Avalonia/ProGPU.Avalonia.csproj" "ProGPU.Avalonia"
}

ensure_packages

rm -rf "${smoke_root}"
mkdir -p "${project_dir}"
export NUGET_PACKAGES="${smoke_root}/packages"

cat >"${project_dir}/Directory.Build.props" <<'XML'
<Project />
XML

cat >"${project_dir}/Directory.Build.targets" <<'XML'
<Project />
XML

cat >"${project_dir}/Directory.Packages.props" <<XML
<Project>
  <Import Project="${repo_root}/external/ProGPU/Directory.Packages.props" />
  <ItemGroup>
    <PackageVersion Include="ProGPU.Avalonia" Version="${progpu_package_version}" />
  </ItemGroup>
</Project>
XML

cat >"${project_dir}/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-progpu" value="${package_output}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat >"${project_dir}/ProGPU.Avalonia.PackageSmoke.csproj" <<XML
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
XML

cat >"${project_dir}/App.axaml" <<'XML'
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ProGPU.Avalonia.PackageSmoke.App">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
XML

cat >"${project_dir}/App.axaml.cs" <<'CS'
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ProGPU.Avalonia.PackageSmoke;

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

cat >"${project_dir}/MainWindow.axaml" <<'XML'
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:progpu="clr-namespace:ProGPU.Avalonia;assembly=ProGPU.Avalonia"
        x:Class="ProGPU.Avalonia.PackageSmoke.MainWindow"
        Title="ProGPU Avalonia Package Smoke"
        Width="640"
        Height="360"
        Background="#111318">
  <Grid RowDefinitions="Auto,*">
    <TextBlock Margin="12"
               Text="ProGPU.Avalonia package smoke"
               Foreground="White" />
    <progpu:ProGpuHostControl x:Name="Host"
                              Grid.Row="1"
                              CornerRadius="4"
                              EnableSharedTextureMemory="False"
                              EnableSharedImageReadback="False" />
  </Grid>
</Window>
XML

cat >"${project_dir}/MainWindow.axaml.cs" <<'CS'
using Avalonia.Controls;
using WinuiGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace ProGPU.Avalonia.PackageSmoke;

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

cat >"${project_dir}/Program.cs" <<'CS'
using System;
using Avalonia;

namespace ProGPU.Avalonia.PackageSmoke;

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

echo "Building external ProGPU Avalonia package smoke..."
set -- \
  "${project_dir}/ProGPU.Avalonia.PackageSmoke.csproj" \
  -v:minimal \
  /p:UseSharedCompilation=false \
  /p:UsedAvaloniaProducts=
if [[ -n "${PROGPU_WPF_NUGET_FALLBACK_PACKAGES:-}" ]]; then
  set -- "$@" -p:RestoreFallbackFolders="${PROGPU_WPF_NUGET_FALLBACK_PACKAGES}"
fi
"${dotnet}" build "$@"

assets_file="${project_dir}/obj/project.assets.json"
if ! grep -q "\"ProGPU.Avalonia/${progpu_package_version}\"" "${assets_file}"; then
  echo "Expected package assets to include ProGPU.Avalonia/${progpu_package_version}." >&2
  exit 1
fi

if ! grep -q "\"ProGPU.WinUI/${progpu_package_version}\"" "${assets_file}"; then
  echo "Expected package assets to include transitive ProGPU.WinUI/${progpu_package_version}." >&2
  exit 1
fi

echo "ProGPU Avalonia package smoke succeeded."
