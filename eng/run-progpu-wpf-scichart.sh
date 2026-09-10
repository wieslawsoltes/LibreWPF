#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

sdk_sample_target_framework="${PROGPU_WPF_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0-windows}"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package="${package_output}/LibreWPF.Sdk.0.1.0-preview.45.nupkg"
directx_package="${package_output}/ProGPU.DirectX.0.1.0-preview.55.nupkg"
scichart_project="${repo_root}/samples/ProGPU.Wpf.SciChartApp/ProGPU.Wpf.SciChartApp.csproj"
scichart_output="${repo_root}/artifacts/bin/ProGPU.Wpf.SciChartApp/Debug/${sdk_sample_target_framework}"
scichart_build_args=(-v:minimal)

if [[ "${PROGPU_WPF_SCICHART_REAL_PACKAGES:-0}" == "1" ]]; then
  scichart_build_args+=("-p:ProGpuWpfUseRealSciChartPackages=true")
fi

apphost_name="ProGPU.Wpf.SciChartApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_SCICHART_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" || ! -f "${directx_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching SciChart Showcase app..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_SHOWCASE_REBUILD_PACKAGES=0 \
  PROGPU_WPF_SHOWCASE_VALIDATE=0 \
  PROGPU_WPF_SHOWCASE_RUN_VALIDATE=0 \
  PROGPU_WPF_SHOWCASE_LIVE_VALIDATE=0 \
  PROGPU_WPF_SCICHART_REBUILD_PACKAGES=0 \
  PROGPU_WPF_SCICHART_VALIDATE=0 \
  PROGPU_WPF_SCICHART_RUN_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.SciChartApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.SciChartApp" \
  "${repo_root}/artifacts/nuget/ProGPU.Wpf.SciChartApp"

echo "Building ProGPU WPF SciChart Showcase app..."
"${dotnet}" build "${scichart_project}" "${scichart_build_args[@]}"

echo "Launching ProGPU WPF SciChart Showcase apphost..."
(
  cd "${scichart_output}"
  "./${apphost_name}" "$@"
)
