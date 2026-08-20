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

hydrate_env_from_launchctl() {
  local name="$1"
  if [[ -n "${!name:-}" ]]; then
    return 0
  fi

  if ! command -v launchctl >/dev/null 2>&1; then
    return 0
  fi

  local value
  value="$(launchctl getenv "${name}" 2>/dev/null || true)"
  if [[ -n "${value}" ]]; then
    export "${name}=${value}"
  fi
}

hydrate_env_from_launchctl "XCEED_TOOLKIT_LICENSE_KEY"
hydrate_env_from_launchctl "XCEED_DATAGRID_LICENSE_KEY"

has_env_or_launchctl() {
  local name="$1"
  if [[ -n "${!name:-}" ]]; then
    return 0
  fi

  if ! command -v launchctl >/dev/null 2>&1; then
    return 1
  fi

  [[ -n "$(launchctl getenv "${name}" 2>/dev/null || true)" ]]
}

sdk_ci_runs_paid_xceed=0
xceed_paid_gate="${PROGPU_WPF_SDK_CI_INCLUDE_XCEED_PAID:-auto}"
if [[ "${xceed_paid_gate}" == "1" ]] || \
   [[ "${xceed_paid_gate}" == "auto" && \
      "$(has_env_or_launchctl XCEED_TOOLKIT_LICENSE_KEY && echo 1 || echo 0)" == "1" && \
      "$(has_env_or_launchctl XCEED_DATAGRID_LICENSE_KEY && echo 1 || echo 0)" == "1" ]]; then
  sdk_ci_runs_paid_xceed=1
fi

validation_requested=0
if [[ "${PROGPU_WPF_XCEED_PAID_VALIDATE:-0}" == "1" || \
      "${PROGPU_WPF_XCEED_PAID_RUN_VALIDATE:-0}" == "1" || \
      "${PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE:-0}" == "1" ]]; then
  validation_requested=1
fi

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package="${package_output}/LibreWPF.Sdk.0.1.0-preview.44.nupkg"
xceed_project="${repo_root}/samples/ProGPU.Wpf.XceedPaidApp/ProGPU.Wpf.XceedPaidApp.csproj"
xceed_output="${repo_root}/artifacts/bin/ProGPU.Wpf.XceedPaidApp/Debug/${sdk_sample_target_framework}"

apphost_name="ProGPU.Wpf.XceedPaidApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_XCEED_PAID_SKIP_REBUILD_PACKAGES:-0}" == "1" ]]; then
  rebuild_packages=0
elif [[ "${PROGPU_WPF_XCEED_PAID_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  rebuild_packages=1
else
  rebuild_packages=0
  for source_path in \
    "${repo_root}/src/ProGPU.Wpf" \
    "${repo_root}/packaging/ProGPU.Wpf.Sdk" \
    "${repo_root}/external/ProGPU/src/ProGPU.Backend" \
    "${repo_root}/external/ProGPU/src/ProGPU.Compute" \
    "${repo_root}/external/ProGPU/src/ProGPU.DirectX" \
    "${repo_root}/external/ProGPU/src/ProGPU.Layout" \
    "${repo_root}/external/ProGPU/src/ProGPU.Scene" \
    "${repo_root}/external/ProGPU/src/ProGPU.Text" \
    "${repo_root}/external/ProGPU/src/ProGPU.Transpiler" \
    "${repo_root}/external/ProGPU/src/ProGPU.Vector" \
    "${repo_root}/external/ProGPU/src/ProGPU.Wpf.Interop" \
    "${repo_root}/external/ProGPU/src/PresentationCore" \
    "${repo_root}/external/ProGPU/src/WindowsBase"; do
    if find "${source_path}" \
      \( -path '*/bin' -o -path '*/obj' \) -prune -o \
      -type f \( -name '*.cs' -o -name '*.props' -o -name '*.targets' -o -name '*.csproj' \) \
      -newer "${sdk_package}" -print -quit | grep -q .; then
      rebuild_packages=1
      break
    fi
  done
fi

if [[ "${rebuild_packages}" == "1" ]]; then
  echo "Building ProGPU WPF SDK packages before launching paid Xceed app..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=0 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES=0 \
  PROGPU_WPF_TOOLKIT_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_RUN_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_LIVE_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"

  if [[ "${validation_requested}" == "1" && "${sdk_ci_runs_paid_xceed}" == "1" ]]; then
    echo "Paid Xceed validation already completed during SDK package rebuild."
    exit 0
  fi
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.XceedPaidApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.XceedPaidApp" \
  "${repo_root}/artifacts/nuget/ProGPU.Wpf.XceedPaidApp"

echo "Building ProGPU WPF paid Xceed app..."
"${dotnet}" build "${xceed_project}" -v:minimal

if [[ "${PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE:-0}" == "1" ]]; then
  echo "Launching ProGPU WPF paid Xceed apphost live geometry probe..."
  live_log="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-xceed-paid-live.XXXXXX")"
  live_status="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-xceed-paid-live-status.XXXXXX")"
  (
    cd "${xceed_output}"
    PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=0 \
    PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE=1 \
    PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE_STATUS_PATH="${live_status}" \
      "./${apphost_name}" "$@" >"${live_log}" 2>&1
  ) &
  apphost_pid="$!"

  live_validation_line=""
  for _ in {1..900}; do
    live_validation_line="$(grep -h -E "ProGPU WPF paid Xceed live geometry validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
    if [[ -n "${live_validation_line}" ]]; then
      break
    fi

    if ! kill -0 "${apphost_pid}" 2>/dev/null; then
      live_validation_line="$(grep -h -E "ProGPU WPF paid Xceed live geometry validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
      if [[ -n "${live_validation_line}" ]]; then
        break
      fi

      echo "Paid Xceed apphost exited before live geometry validation succeeded." >&2
      cat "${live_status}" >&2
      cat "${live_log}" >&2
      exit 1
    fi

    sleep 0.1
  done

  if kill -0 "${apphost_pid}" 2>/dev/null; then
    kill "${apphost_pid}" 2>/dev/null || true
    wait "${apphost_pid}" 2>/dev/null || true
  fi

  if [[ -z "${live_validation_line}" ]]; then
    echo "Expected paid Xceed apphost live geometry validation to succeed." >&2
    cat "${live_status}" >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if [[ "${live_validation_line}" =~ logical[[:space:]]([0-9]+)x([0-9]+),[[:space:]]pixels[[:space:]]([0-9]+)x([0-9]+),[[:space:]]viewport[[:space:]]([0-9]+)x([0-9]+)@([0-9]+),([0-9]+),[[:space:]]dpi[[:space:]]([0-9]+(\.[0-9]+)?) ]]; then
    logical_width="${BASH_REMATCH[1]}"
    logical_height="${BASH_REMATCH[2]}"
    pixel_width="${BASH_REMATCH[3]}"
    pixel_height="${BASH_REMATCH[4]}"
    viewport_width="${BASH_REMATCH[5]}"
    viewport_height="${BASH_REMATCH[6]}"
    viewport_x="${BASH_REMATCH[7]}"
    viewport_y="${BASH_REMATCH[8]}"
    if (( logical_width <= 0 || logical_height <= 0 || pixel_width < logical_width || pixel_height < logical_height )); then
      echo "Expected paid Xceed apphost pixels to cover logical content, but got logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}." >&2
      cat "${live_log}" >&2
      exit 1
    fi

    if (( viewport_x != 0 || viewport_y != 0 || viewport_width != pixel_width || viewport_height != pixel_height )); then
      echo "Expected paid Xceed apphost viewport to cover full physical target, but got viewport ${viewport_width}x${viewport_height}@${viewport_x},${viewport_y}, pixels ${pixel_width}x${pixel_height}." >&2
      cat "${live_log}" >&2
      exit 1
    fi
  else
    echo "Could not parse paid Xceed apphost geometry from live validation line." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  echo "${live_validation_line}"
  rm -f "${live_log}" "${live_status}"
  exit 0
fi

if [[ "${PROGPU_WPF_XCEED_PAID_VALIDATE:-0}" == "1" || "${PROGPU_WPF_XCEED_PAID_RUN_VALIDATE:-0}" == "1" ]]; then
  echo "Running ProGPU WPF paid Xceed apphost validation..."
  (
    cd "${xceed_output}"
    "./${apphost_name}" "$@"
  )
  exit 0
fi

echo "Launching ProGPU WPF paid Xceed apphost..."
(
  cd "${xceed_output}"
  "./${apphost_name}" "$@"
)
