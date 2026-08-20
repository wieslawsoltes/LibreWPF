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

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package="${package_output}/LibreWPF.Sdk.0.1.0-preview.43.nupkg"
if [[ "${PROGPU_WPF_MVP_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before quickcheck..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=0 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

echo "Running external no-source-change ProGPU WPF SDK smoke..."
"${dotnet}" run \
  --project "${repo_root}/src/ProGPU.Wpf.SdkExternalSmokeHarness/ProGPU.Wpf.SdkExternalSmokeHarness.csproj" \
  -v:minimal

echo "Building SDK-switch smoke app..."
"${dotnet}" build "${repo_root}/src/ProGPU.Wpf.SdkSwitchSmoke/ProGPU.Wpf.SdkSwitchSmoke.csproj" -v:minimal

sdk_switch_output="${repo_root}/artifacts/bin/ProGPU.Wpf.SdkSwitchSmoke/Debug/${sdk_sample_target_framework}"
sdk_switch_apphost_name="$(apphost_name "ProGPU.Wpf.SdkSwitchSmoke")"
if [[ ! -x "${sdk_switch_output}/${sdk_switch_apphost_name}" ]]; then
  echo "Expected SDK-switch smoke apphost at ${sdk_switch_output}/${sdk_switch_apphost_name}" >&2
  exit 1
fi

echo "Running SDK-switch smoke apphost live input probe..."
(
  cd "${sdk_switch_output}"
  PROGPU_WPF_SDK_SWITCH_LIVE_VALIDATE=1 "./${sdk_switch_apphost_name}"
)

echo "Running Hello SDK apphost Application.Run self-test..."
PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
PROGPU_WPF_HELLO_RUN_VALIDATE=1 \
PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  "${repo_root}/eng/run-progpu-wpf-hello.sh"

echo "Running Hello SDK apphost live geometry probe..."
PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
PROGPU_WPF_HELLO_LIVE_VALIDATE=1 \
  "${repo_root}/eng/run-progpu-wpf-hello.sh"

echo "Running MVP SDK apphost Application.Run self-test..."
PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
PROGPU_WPF_MVP_VALIDATE=0 \
PROGPU_WPF_MVP_RUN_VALIDATE=1 \
PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
  "${repo_root}/eng/run-progpu-wpf-mvp.sh"

echo "Running MVP SDK apphost live geometry probe..."
PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
PROGPU_WPF_MVP_VALIDATE=0 \
PROGPU_WPF_MVP_RUN_VALIDATE=0 \
PROGPU_WPF_MVP_LIVE_VALIDATE=1 \
  "${repo_root}/eng/run-progpu-wpf-mvp.sh"

echo "ProGPU WPF MVP quickcheck succeeded."
