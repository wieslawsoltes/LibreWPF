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

sdk_sample_target_framework="${PROGPU_WPF_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0-windows}"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package="${package_output}/LibreWPF.Sdk.0.1.0-preview.44.nupkg"
toolkit_project="${repo_root}/samples/ProGPU.Wpf.ToolkitApp/ProGPU.Wpf.ToolkitApp.csproj"
toolkit_output="${repo_root}/artifacts/bin/ProGPU.Wpf.ToolkitApp/Debug/${sdk_sample_target_framework}"

apphost_name="ProGPU.Wpf.ToolkitApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching Toolkit app..."
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
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.ToolkitApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.ToolkitApp" \
  "${repo_root}/artifacts/nuget/ProGPU.Wpf.ToolkitApp"

echo "Building ProGPU WPF Toolkit app..."
"${dotnet}" build "${toolkit_project}" -v:minimal

if [[ "$(uname -s 2>/dev/null || echo unknown)" == "Linux" ]]; then
  case "$(uname -m 2>/dev/null || echo unknown)" in
    x86_64)
      native_runtime_id="linux-x64"
      ;;
    aarch64|arm64)
      native_runtime_id="linux-arm64"
      ;;
    armv7l|armv8l)
      native_runtime_id="linux-arm"
      ;;
    *)
      native_runtime_id=""
      ;;
  esac

  if [[ -n "${native_runtime_id}" ]]; then
    native_runtime_dir="${toolkit_output}/runtimes/${native_runtime_id}/native"
    if [[ -d "${native_runtime_dir}" ]]; then
      export LD_LIBRARY_PATH="${native_runtime_dir}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
    fi
  fi
fi

if [[ "${PROGPU_WPF_TOOLKIT_LIVE_VALIDATE:-0}" == "1" ]]; then
  export PROGPU_WPF_TOOLKIT_LIVE_VALIDATE
  live_log="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-toolkit-live.XXXXXX")"
  live_status="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-toolkit-live-status.XXXXXX")"
  apphost_pid=""
  cleanup_live_probe() {
    if [[ -n "${apphost_pid}" ]] && kill -0 "${apphost_pid}" 2>/dev/null; then
      kill "${apphost_pid}" 2>/dev/null || true
      sleep 0.5
      if kill -0 "${apphost_pid}" 2>/dev/null; then
        kill -9 "${apphost_pid}" 2>/dev/null || true
      fi
      wait "${apphost_pid}" 2>/dev/null || true
    fi
    rm -f "${live_log}" "${live_status}"
  }
  trap cleanup_live_probe EXIT

  echo "Launching ProGPU WPF Toolkit apphost live geometry probe..."
  (
    cd "${toolkit_output}"
    PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_STATUS_PATH="${live_status}" "./${apphost_name}" "$@"
  ) >"${live_log}" 2>&1 &
  apphost_pid="$!"

  live_validation_timeout_seconds="${PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_TIMEOUT_SECONDS:-180}"
  if [[ ! "${live_validation_timeout_seconds}" =~ ^[1-9][0-9]*$ ]]; then
    echo "Invalid PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_TIMEOUT_SECONDS value '${live_validation_timeout_seconds}'. Expected a positive integer." >&2
    exit 1
  fi

  live_validation_line=""
  live_validation_deadline=$((SECONDS + live_validation_timeout_seconds))
  while (( SECONDS < live_validation_deadline )); do
    live_validation_line="$(grep -h -E "ProGPU WPF Toolkit live input validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
    if [[ -n "${live_validation_line}" ]]; then
      break
    fi

    if ! kill -0 "${apphost_pid}" 2>/dev/null; then
      live_validation_line="$(grep -h -E "ProGPU WPF Toolkit live input validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
      if [[ -n "${live_validation_line}" ]]; then
        break
      fi

      echo "Toolkit apphost exited before live input validation succeeded." >&2
      cat "${live_status}" >&2
      cat "${live_log}" >&2
      exit 1
    fi

    sleep 0.1
  done

  if [[ -z "${live_validation_line}" ]]; then
    echo "Expected Toolkit apphost live input validation to succeed before timeout." >&2
    cat "${live_status}" >&2
    cat "${live_log}" >&2
    exit 1
  fi

  logical_width=980
  logical_height=640
  if [[ "${live_validation_line}" =~ logical[[:space:]]([0-9]+)x([0-9]+),[[:space:]]pixels[[:space:]]([0-9]+)x([0-9]+),[[:space:]]viewport[[:space:]]([0-9]+)x([0-9]+)@([0-9]+),([0-9]+),[[:space:]]dpi[[:space:]]([0-9]+(\.[0-9]+)?) ]]; then
    logical_width="${BASH_REMATCH[1]}"
    logical_height="${BASH_REMATCH[2]}"
    pixel_width="${BASH_REMATCH[3]}"
    pixel_height="${BASH_REMATCH[4]}"
    viewport_width="${BASH_REMATCH[5]}"
    viewport_height="${BASH_REMATCH[6]}"
    viewport_x="${BASH_REMATCH[7]}"
    viewport_y="${BASH_REMATCH[8]}"
    if (( viewport_x != 0 || viewport_y != 0 || viewport_width != pixel_width || viewport_height != pixel_height )); then
      echo "Expected Toolkit apphost viewport to cover full pixel target, but got viewport ${viewport_width}x${viewport_height}@${viewport_x},${viewport_y} for ${pixel_width}x${pixel_height} pixels." >&2
      cat "${live_log}" >&2
      exit 1
    fi
  else
    echo "Could not parse Toolkit apphost geometry from live validation line." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( logical_width != 980 || logical_height != 640 )); then
    echo "Expected Toolkit apphost logical size to be 980x640, but got ${logical_width}x${logical_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( pixel_width < logical_width || pixel_height < logical_height )); then
    echo "Expected Toolkit apphost pixels to cover ${logical_width}x${logical_height} logical content, but got ${pixel_width}x${pixel_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  trap - EXIT
  cleanup_live_probe >/dev/null 2>&1
  echo "${live_validation_line}"
  echo "ProGPU WPF Toolkit live geometry validation succeeded: logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}."
  exit 0
fi

if [[ "${PROGPU_WPF_TOOLKIT_VALIDATE:-0}" == "1" || "${PROGPU_WPF_TOOLKIT_RUN_VALIDATE:-0}" == "1" ]]; then
  echo "Running ProGPU WPF Toolkit apphost validation..."
  (
    cd "${toolkit_output}"
    "./${apphost_name}" "$@"
  )
  exit 0
fi

echo "Launching ProGPU WPF Toolkit apphost..."
(
  cd "${toolkit_output}"
  "./${apphost_name}" "$@"
)
