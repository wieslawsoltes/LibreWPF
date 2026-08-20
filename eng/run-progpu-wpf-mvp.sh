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
sdk_package="${package_output}/LibreWPF.Sdk.0.1.0-preview.43.nupkg"
mvp_project="${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj"
mvp_output="${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp/Debug/${sdk_sample_target_framework}"

apphost_name="ProGPU.Wpf.MvpApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_MVP_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching MVP app..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=0 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

if [[ "${PROGPU_WPF_MVP_SKIP_BUILD:-0}" != "1" ]]; then
  rm -rf \
    "${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp" \
    "${repo_root}/artifacts/obj/ProGPU.Wpf.MvpApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.MvpApp"

  echo "Building ProGPU WPF MVP app..."
  "${dotnet}" build "${mvp_project}" -v:minimal
elif [[ ! -x "${mvp_output}/${apphost_name}" ]]; then
  echo "Expected prebuilt MVP apphost at ${mvp_output}/${apphost_name}." >&2
  exit 1
fi

if [[ "${PROGPU_WPF_MVP_LIVE_VALIDATE:-0}" == "1" ]]; then
  export PROGPU_WPF_MVP_LIVE_VALIDATE
  if [[ "${PROGPU_WPF_MVP_TRACE_RENDER_SURFACE:-0}" == "1" ]]; then
    export PROGPU_WPF_TRACE_RENDER_SURFACE=1
  fi
  live_log="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-mvp-live.XXXXXX")"
  live_status="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-mvp-live-status.XXXXXX")"
  apphost_pid=""
  live_validation_timeout_seconds="${PROGPU_WPF_MVP_LIVE_VALIDATE_TIMEOUT_SECONDS:-60}"
  if [[ ! "${live_validation_timeout_seconds}" =~ ^[1-9][0-9]*$ ]]; then
    echo "Invalid PROGPU_WPF_MVP_LIVE_VALIDATE_TIMEOUT_SECONDS value '${live_validation_timeout_seconds}'." >&2
    exit 1
  fi
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

  echo "Launching ProGPU WPF MVP apphost live geometry probe..."
  (
    cd "${mvp_output}"
    if (($# > 0)); then
      PROGPU_WPF_MVP_LIVE_VALIDATE_STATUS_PATH="${live_status}" "./${apphost_name}" "$@"
    else
      PROGPU_WPF_MVP_LIVE_VALIDATE_STATUS_PATH="${live_status}" "./${apphost_name}"
    fi
  ) >"${live_log}" 2>&1 &
  apphost_pid="$!"

  live_validation_line=""
  render_surface_line=""
  live_validation_deadline=$((SECONDS + live_validation_timeout_seconds))
  while (( SECONDS < live_validation_deadline )); do
    live_validation_line="$(grep -h -E "ProGPU WPF MVP live input validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
    render_surface_line="$(grep -E "ProGPU WPF render surface:" "${live_log}" | tail -n 1 || true)"
    if [[ -n "${live_validation_line}" ]]; then
      break
    fi

    if ! kill -0 "${apphost_pid}" 2>/dev/null; then
      live_validation_line="$(grep -h -E "ProGPU WPF MVP live input validation succeeded:" "${live_status}" "${live_log}" 2>/dev/null | tail -n 1 || true)"
      render_surface_line="$(grep -E "ProGPU WPF render surface:" "${live_log}" | tail -n 1 || true)"
      if [[ -n "${live_validation_line}" ]]; then
        break
      fi

      echo "MVP apphost exited before live input validation succeeded." >&2
      if [[ -f "${live_status}" ]]; then
        cat "${live_status}" >&2
      fi
      cat "${live_log}" >&2
      exit 1
    fi

    sleep 0.05
  done

  if [[ -z "${live_validation_line}" ]]; then
    echo "Expected MVP apphost live input validation to succeed before timeout." >&2
    if [[ -f "${live_status}" ]]; then
      cat "${live_status}" >&2
    fi
    cat "${live_log}" >&2
    exit 1
  fi

  logical_width=760
  logical_height=560
  viewport_width=""
  viewport_height=""
  viewport_x=""
  viewport_y=""
  has_viewport_geometry=0

  if [[ -n "${render_surface_line}" ]]; then
    if [[ ! "${render_surface_line}" =~ logical[[:space:]]([0-9]+)x([0-9]+),[[:space:]]pixels[[:space:]]([0-9]+)x([0-9]+),[[:space:]]viewport[[:space:]]([0-9]+)x([0-9]+)@([0-9]+),([0-9]+),[[:space:]]dpi[[:space:]]([0-9]+(\.[0-9]+)?) ]]; then
      echo "Could not parse MVP apphost render-surface line: ${render_surface_line}" >&2
      cat "${live_log}" >&2
      exit 1
    fi

    logical_width="${BASH_REMATCH[1]}"
    logical_height="${BASH_REMATCH[2]}"
    pixel_width="${BASH_REMATCH[3]}"
    pixel_height="${BASH_REMATCH[4]}"
    viewport_width="${BASH_REMATCH[5]}"
    viewport_height="${BASH_REMATCH[6]}"
    viewport_x="${BASH_REMATCH[7]}"
    viewport_y="${BASH_REMATCH[8]}"
    has_viewport_geometry=1
  elif [[ "${live_validation_line}" =~ logical[[:space:]]([0-9]+)x([0-9]+),[[:space:]]pixels[[:space:]]([0-9]+)x([0-9]+),[[:space:]]viewport[[:space:]]([0-9]+)x([0-9]+)@([0-9]+),([0-9]+),[[:space:]]dpi[[:space:]]([0-9]+(\.[0-9]+)?) ]]; then
    logical_width="${BASH_REMATCH[1]}"
    logical_height="${BASH_REMATCH[2]}"
    pixel_width="${BASH_REMATCH[3]}"
    pixel_height="${BASH_REMATCH[4]}"
    viewport_width="${BASH_REMATCH[5]}"
    viewport_height="${BASH_REMATCH[6]}"
    viewport_x="${BASH_REMATCH[7]}"
    viewport_y="${BASH_REMATCH[8]}"
    has_viewport_geometry=1
  else
    echo "Could not parse MVP apphost geometry from live validation or render-surface line." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( logical_width != 760 || logical_height != 560 )); then
    echo "Expected MVP apphost logical size to be 760x560, but got ${logical_width}x${logical_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( pixel_width < logical_width || pixel_height < logical_height )); then
    echo "Expected MVP apphost pixels to cover ${logical_width}x${logical_height} logical content, but got ${pixel_width}x${pixel_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( has_viewport_geometry == 1 )) && (( viewport_x != 0 || viewport_y != 0 || viewport_width != pixel_width || viewport_height != pixel_height )); then
    echo "Expected MVP apphost viewport to use full physical target, but got ${viewport_width}x${viewport_height}@${viewport_x},${viewport_y} for pixels ${pixel_width}x${pixel_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  trap - EXIT
  if [[ -s "${live_status}" ]]; then
    cat "${live_status}"
  else
    echo "${live_validation_line}"
  fi
  if (( has_viewport_geometry == 1 )); then
    echo "ProGPU WPF MVP live geometry validation succeeded: logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}, viewport ${viewport_width}x${viewport_height}@${viewport_x},${viewport_y}."
  else
    echo "ProGPU WPF MVP live geometry validation succeeded: logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}."
  fi
  cleanup_live_probe >/dev/null 2>&1
  exit 0
fi

echo "Launching ProGPU WPF MVP apphost..."
(
  cd "${mvp_output}"
  if (($# > 0)); then
    "./${apphost_name}" "$@"
  else
    "./${apphost_name}"
  fi
)
