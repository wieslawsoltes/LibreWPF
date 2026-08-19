#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

if [[ "${dotnet}" == "${repo_root}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-${repo_root}/.dotnet}"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

resolve_dotnet_runtime_framework_version() {
  local runtime_version
  runtime_version="$("${dotnet}" --list-runtimes 2>/dev/null | awk '$1 == "Microsoft.NETCore.App" && $2 ~ /^11\./ { version = $2 } END { print version }')"
  if [[ -z "${runtime_version}" ]]; then
    runtime_version="$("${dotnet}" --list-runtimes 2>/dev/null | awk '$1 == "Microsoft.NETCore.App" { version = $2 } END { print version }')"
  fi
  printf '%s\n' "${runtime_version}"
}

if [[ -z "${ProGpuWpfRuntimeFrameworkVersion:-}" ]]; then
  validation_runtime_version="$(resolve_dotnet_runtime_framework_version)"
  if [[ -n "${validation_runtime_version}" ]]; then
    export ProGpuWpfRuntimeFrameworkVersion="${validation_runtime_version}"
    echo "Using ProGpuWpfRuntimeFrameworkVersion=${ProGpuWpfRuntimeFrameworkVersion} for SDK validation apphosts."
  fi
fi

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.42}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.52}"
prepackaged_progpu_dir="${PROGPU_WPF_PREPACKAGED_PROGPU_DIR:-}"
progpu_package_snapshot_dir="${repo_root}/artifacts/progpu-wpf-sdk-smoke/exact-progpu-packages"
sdk_sample_target_framework="${PROGPU_WPF_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0-windows}"
mkdir -p "${package_output}"

clean_preview_package_output() {
  rm -f \
    "${package_output}"/*.nupkg \
    "${package_output}"/*.snupkg \
    "${package_output}"/*-preview-*.tar.gz \
    "${package_output}"/*-preview-*.tar.gz.sha256 \
    "${package_output}"/*-preview-packages-*.json \
    "${package_output}/README.md" \
    "${package_output}/NuGet.config"
}

pack_project() {
  local project="$1"
  local package_id="$2"
  local package_version="${3:-${dev_package_version}}"
  rm -f \
    "${package_output}/${package_id}.${package_version}.nupkg" \
    "${package_output}/${package_id}.${package_version}.snupkg"
  run_dotnet pack "${repo_root}/${project}" \
    -c Release \
    -o "${package_output}" \
    -v:minimal \
    -p:Version="${package_version}" \
    -p:PackageVersion="${package_version}"
}

stage_or_pack_progpu_project() {
  local project="$1"
  local package_id="$2"

  if [[ -z "${prepackaged_progpu_dir}" ]]; then
    pack_project "${project}" "${package_id}" "${progpu_package_version}"
    return
  fi

  local source_package="${prepackaged_progpu_dir}/${package_id}.${progpu_package_version}.nupkg"
  local destination_package="${package_output}/${package_id}.${progpu_package_version}.nupkg"
  if [[ ! -f "${source_package}" ]]; then
    echo "Missing exact ProGPU release package ${source_package}." >&2
    exit 1
  fi

  cp "${source_package}" "${destination_package}"
  cmp "${source_package}" "${destination_package}"
}

snapshot_staged_progpu_packages() {
  local package_id
  local source_package
  local snapshot_package

  rm -rf "${progpu_package_snapshot_dir}"
  mkdir -p "${progpu_package_snapshot_dir}"

  for package_id in \
    ProGPU.Backend \
    ProGPU.Backend.Dawn \
    ProGPU.Text.Shaping \
    ProGPU.DirectX \
    ProGPU.Transpiler \
    ProGPU.Compute \
    ProGPU.Vector \
    ProGPU.Text \
    ProGPU.Scene \
    ProGPU.Layout \
    ProGPU.Virtualization \
    ProGPU.WinRT \
    ProGPU.Media \
    ProGPU.Media.Scene \
    ProGPU.WinUI \
    ProGPU.Avalonia \
    ProGPU.SkiaSharp \
    ProGPU.System.Drawing.Common \
    LibreWPF.Interop
  do
    source_package="${package_output}/${package_id}.${progpu_package_version}.nupkg"
    snapshot_package="${progpu_package_snapshot_dir}/${package_id}.${progpu_package_version}.nupkg"
    if [[ ! -f "${source_package}" ]]; then
      echo "Missing staged ProGPU package ${source_package}." >&2
      exit 1
    fi

    cp "${source_package}" "${snapshot_package}"
    cmp "${source_package}" "${snapshot_package}"
  done

  export PROGPU_WPF_PREPACKAGED_PROGPU_DIR="${progpu_package_snapshot_dir}"
}

run_dotnet() {
  local command="$1"
  shift
  case "${command}" in
    msbuild|build|pack|run)
      if [[ "${PROGPU_WPF_SERIAL_BUILD:-0}" == "1" ]]; then
        "${dotnet}" "${command}" -m:1 -p:UseSharedCompilation=false "$@"
      else
        "${dotnet}" "${command}" "$@"
      fi
      ;;
    *)
      "${dotnet}" "${command}" "$@"
      ;;
  esac
}

has_env_or_launchctl() {
  local name="$1"
  if [[ -n "${!name:-}" ]]; then
    return 0
  fi

  if command -v launchctl >/dev/null 2>&1 && [[ -n "$(launchctl getenv "${name}" 2>/dev/null || true)" ]]; then
    return 0
  fi

  return 1
}

apphost_name() {
  local assembly_name="$1"
  case "$(uname -s 2>/dev/null || echo unknown)" in
    MINGW*|MSYS*|CYGWIN*)
      echo "${assembly_name}.exe"
      ;;
    *)
      echo "${assembly_name}"
      ;;
  esac
}

clean_sdk_smoke_outputs() {
  local project
  for project in \
    "ProGPU.Wpf.SdkSwitchLibrary" \
    "ProGPU.Wpf.SdkSwitchSmoke" \
    "ProGPU.Wpf.SdkMixedDesktopSmoke" \
    "ProGPU.Wpf.SdkSwitchRuntimeHarness" \
    "ProGPU.Wpf.SdkExternalSmokeHarness" \
    "ProGPU.Wpf.HelloApp" \
    "ProGPU.Wpf.MvpApp" \
    "ProGPU.Wpf.ToolkitApp" \
    "ProGPU.Wpf.XceedPaidApp" \
    "ProGPU.Wpf.SciChartMvpApp"
  do
    rm -rf \
      "${repo_root}/artifacts/bin/${project}" \
      "${repo_root}/artifacts/obj/${project}"
  done

  rm -rf \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.SdkSwitchSmoke" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.HelloApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.MvpApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.ToolkitApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.XceedPaidApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.SciChartMvpApp"
}

clean_preview_package_output

# Managed WPF projects aggregate their package payload into one shared staging
# directory. Clear that directory before the first project builds so a prior
# target framework or a removed assembly cannot leak into this package run.
rm -rf "${repo_root}/artifacts/packaging/Release/LibreWPF.Transport"

echo "Staging exact ProGPU packages for the LibreWPF.Sdk feed..."
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Backend/ProGPU.Backend.csproj" "ProGPU.Backend"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Backend.Dawn/ProGPU.Backend.Dawn.csproj" "ProGPU.Backend.Dawn"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Text.Shaping/ProGPU.Text.Shaping.csproj" "ProGPU.Text.Shaping"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.DirectX/ProGPU.DirectX.csproj" "ProGPU.DirectX"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Transpiler/ProGPU.Transpiler.csproj" "ProGPU.Transpiler"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Compute/ProGPU.Compute.csproj" "ProGPU.Compute"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Vector/ProGPU.Vector.csproj" "ProGPU.Vector"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Text/ProGPU.Text.csproj" "ProGPU.Text"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Scene/ProGPU.Scene.csproj" "ProGPU.Scene"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Layout/ProGPU.Layout.csproj" "ProGPU.Layout"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Virtualization/ProGPU.Virtualization.csproj" "ProGPU.Virtualization"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.WinRT/ProGPU.WinRT.csproj" "ProGPU.WinRT"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Media/ProGPU.Media.csproj" "ProGPU.Media"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Media.Scene/ProGPU.Media.Scene.csproj" "ProGPU.Media.Scene"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.WinUI/ProGPU.WinUI.csproj" "ProGPU.WinUI"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Avalonia/ProGPU.Avalonia.csproj" "ProGPU.Avalonia"
stage_or_pack_progpu_project "external/ProGPU/src/SkiaSharp/SkiaSharp.csproj" "ProGPU.SkiaSharp"
stage_or_pack_progpu_project "external/ProGPU/src/System.Drawing.Common/System.Drawing.Common.csproj" "ProGPU.System.Drawing.Common"
stage_or_pack_progpu_project "external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj" "LibreWPF.Interop"
snapshot_staged_progpu_packages

echo "Running ProGPU Avalonia package consumer smoke..."
"${repo_root}/eng/progpu-avalonia-package-smoke.sh"

echo "Building managed WPF transport payload..."
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:RestoreManagedTransport \
  -property:Configuration=Release \
  -verbosity:minimal
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:BuildManagedTransport \
  -property:Configuration=Release \
  -verbosity:minimal
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:RestoreThemes \
  -property:Configuration=Release \
  -verbosity:minimal
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:BuildThemes \
  -property:Configuration=Release \
  -verbosity:minimal

echo "Building real WPF validation harnesses..."
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:RestoreHarnesses \
  -property:Configuration=Release \
  -verbosity:minimal
run_dotnet msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:BuildHarnesses \
  -property:Configuration=Release \
  -verbosity:minimal

echo "Running real WPF XAML runtime harness..."
run_dotnet run --no-build --project "${repo_root}/src/ProGPU.Wpf.RealXamlRuntimeHarness/ProGPU.Wpf.RealXamlRuntimeHarness.csproj" -c Release -v:minimal

echo "Running real WPF Application.Run harness..."
run_dotnet run --no-build --project "${repo_root}/src/ProGPU.Wpf.RealApplicationRunHarness/ProGPU.Wpf.RealApplicationRunHarness.csproj" -c Release -v:minimal

echo "Running real WPF Fluent theme runtime harness..."
run_dotnet run --no-build --project "${repo_root}/src/ProGPU.Wpf.RealThemeRuntimeHarness/ProGPU.Wpf.RealThemeRuntimeHarness.csproj" -c Release -v:minimal

echo "Packing LibreWPF transport, ProGPU bridge, and custom SDK..."
pack_project "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj" "LibreWPF.Transport"
pack_project "src/ProGPU.Wpf/ProGPU.Wpf.csproj" "LibreWPF.ProGPU"
pack_project "packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj" "LibreWPF.Sdk"

echo "Auditing preview package artifacts..."
"${repo_root}/eng/progpu-preview-package-audit.sh"

echo "Writing preview package manifest..."
"${repo_root}/eng/progpu-preview-package-manifest.sh"

echo "Writing preview release bundle..."
"${repo_root}/eng/progpu-preview-release-bundle.sh"

echo "Verifying preview release bundle..."
"${repo_root}/eng/progpu-preview-release-verify.sh"

echo "Running preview release bundle SDK smoke..."
"${repo_root}/eng/progpu-preview-release-sdk-smoke.sh"

echo "Cleaning package-mode SDK smoke outputs..."
clean_sdk_smoke_outputs

echo "Building package-mode SDK switch smoke..."
run_dotnet build "${repo_root}/src/ProGPU.Wpf.SdkSwitchSmoke/ProGPU.Wpf.SdkSwitchSmoke.csproj" -v:minimal

echo "Building and running mixed WPF/WinForms SDK smoke app..."
run_dotnet build "${repo_root}/src/ProGPU.Wpf.SdkSwitchSmoke/MixedDesktop/ProGPU.Wpf.SdkMixedDesktopSmoke.csproj" -v:minimal
run_dotnet run --no-build --project "${repo_root}/src/ProGPU.Wpf.SdkSwitchSmoke/MixedDesktop/ProGPU.Wpf.SdkMixedDesktopSmoke.csproj" -v:minimal

echo "Running SDK switch runtime smoke..."
run_dotnet run --project "${repo_root}/src/ProGPU.Wpf.SdkSwitchRuntimeHarness/ProGPU.Wpf.SdkSwitchRuntimeHarness.csproj" -v:minimal

echo "Running external no-source-change SDK smoke..."
run_dotnet run --project "${repo_root}/src/ProGPU.Wpf.SdkExternalSmokeHarness/ProGPU.Wpf.SdkExternalSmokeHarness.csproj" -v:minimal

echo "Building Hello SDK app..."
run_dotnet build "${repo_root}/samples/ProGPU.Wpf.HelloApp/ProGPU.Wpf.HelloApp.csproj" -v:minimal

hello_output="${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp/Debug/${sdk_sample_target_framework}"
hello_apphost_name="$(apphost_name "ProGPU.Wpf.HelloApp")"
if [[ ! -x "${hello_output}/${hello_apphost_name}" ]]; then
  echo "Expected Hello SDK apphost at ${hello_output}/${hello_apphost_name}" >&2
  exit 1
fi

echo "Running Hello SDK app apphost Application.Run validation..."
(
  cd "${hello_output}"
  PROGPU_WPF_HELLO_RUN_VALIDATE=1 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
    "./${hello_apphost_name}" "hello-alpha" "hello beta"
)

echo "Running Hello SDK app live geometry validation..."
PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
PROGPU_WPF_HELLO_SKIP_BUILD=1 \
PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
PROGPU_WPF_HELLO_LIVE_VALIDATE=1 \
  "${repo_root}/eng/run-progpu-wpf-hello.sh"

echo "Building MVP SDK app..."
run_dotnet build "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

echo "Running MVP SDK app validation..."
PROGPU_WPF_MVP_VALIDATE=1 \
PROGPU_WPF_MVP_RUN_VALIDATE=0 \
PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
  run_dotnet run --no-build --project "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

echo "Running MVP SDK app Application.Run validation..."
PROGPU_WPF_MVP_VALIDATE=0 \
PROGPU_WPF_MVP_RUN_VALIDATE=1 \
PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
  run_dotnet run --no-build --project "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

mvp_output="${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp/Debug/${sdk_sample_target_framework}"
mvp_apphost_name="$(apphost_name "ProGPU.Wpf.MvpApp")"
if [[ ! -x "${mvp_output}/${mvp_apphost_name}" ]]; then
  echo "Expected MVP SDK apphost at ${mvp_output}/${mvp_apphost_name}" >&2
  exit 1
fi

echo "Running MVP SDK app apphost Application.Run validation..."
(
  cd "${mvp_output}"
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=1 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
    "./${mvp_apphost_name}"
)

echo "Running MVP SDK app live geometry validation..."
PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
PROGPU_WPF_MVP_SKIP_BUILD=1 \
PROGPU_WPF_MVP_VALIDATE=0 \
PROGPU_WPF_MVP_RUN_VALIDATE=0 \
PROGPU_WPF_MVP_LIVE_VALIDATE=1 \
PROGPU_WPF_MVP_PERFORMANCE_VALIDATE=1 \
  "${repo_root}/eng/run-progpu-wpf-mvp.sh"

echo "Running Toolkit SDK app live validation..."
PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES=0 \
PROGPU_WPF_TOOLKIT_VALIDATE=0 \
PROGPU_WPF_TOOLKIT_RUN_VALIDATE=0 \
PROGPU_WPF_TOOLKIT_LIVE_VALIDATE=1 \
  "${repo_root}/eng/run-progpu-wpf-toolkit.sh"

xceed_paid_gate="${PROGPU_WPF_SDK_CI_INCLUDE_XCEED_PAID:-auto}"
if [[ "${xceed_paid_gate}" == "1" ]] || \
   [[ "${xceed_paid_gate}" == "auto" && \
      "$(has_env_or_launchctl XCEED_TOOLKIT_LICENSE_KEY && echo 1 || echo 0)" == "1" && \
      "$(has_env_or_launchctl XCEED_DATAGRID_LICENSE_KEY && echo 1 || echo 0)" == "1" ]]; then
  echo "Running paid Xceed SDK app live validation..."
  PROGPU_WPF_XCEED_PAID_SKIP_REBUILD_PACKAGES=1 \
  PROGPU_WPF_XCEED_PAID_VALIDATE=0 \
  PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=0 \
  PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE=1 \
    "${repo_root}/eng/run-progpu-wpf-xceed-paid.sh"
elif [[ "${xceed_paid_gate}" == "0" || "${xceed_paid_gate}" == "auto" ]]; then
  echo "Skipping paid Xceed SDK app validation because paid license environment is unavailable."
else
  echo "Invalid PROGPU_WPF_SDK_CI_INCLUDE_XCEED_PAID value '${xceed_paid_gate}'. Expected 0, 1, or auto." >&2
  exit 1
fi

echo "Building SciChart MVP SDK app..."
run_dotnet build "${repo_root}/samples/ProGPU.Wpf.SciChartMvpApp/ProGPU.Wpf.SciChartMvpApp.csproj" -v:minimal

echo "Running SciChart MVP SDK app renderer validation..."
PROGPU_WPF_SCICHART_VALIDATE=1 \
PROGPU_WPF_SCICHART_RUN_VALIDATE=0 \
  run_dotnet run --no-build --project "${repo_root}/samples/ProGPU.Wpf.SciChartMvpApp/ProGPU.Wpf.SciChartMvpApp.csproj" -v:minimal

echo "Running SciChart MVP SDK app Application.Run validation..."
PROGPU_WPF_SCICHART_VALIDATE=0 \
PROGPU_WPF_SCICHART_RUN_VALIDATE=1 \
  run_dotnet run --no-build --project "${repo_root}/samples/ProGPU.Wpf.SciChartMvpApp/ProGPU.Wpf.SciChartMvpApp.csproj" -v:minimal

echo "Building focused WPF graph tests..."
run_dotnet build "${repo_root}/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj" -v:minimal

echo "Running focused SDK graph guard..."
run_dotnet vstest \
  "${repo_root}/src/ProGPU.Wpf.Tests/bin/Debug/net10.0/ProGPU.Wpf.Tests.dll" \
  --Tests:ProGPU.Wpf.Tests.Composition.WpfManagedProjectGraphTests.ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface
