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
hello_project="${repo_root}/samples/ProGPU.Wpf.HelloApp/ProGPU.Wpf.HelloApp.csproj"
hello_output="${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp/Debug/${sdk_sample_target_framework}"

apphost_name="ProGPU.Wpf.HelloApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_HELLO_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching Hello app..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=0 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

if [[ "${PROGPU_WPF_HELLO_SKIP_BUILD:-0}" != "1" ]]; then
  rm -rf \
    "${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp" \
    "${repo_root}/artifacts/obj/ProGPU.Wpf.HelloApp" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.HelloApp"

  echo "Building ProGPU WPF Hello app..."
  "${dotnet}" build "${hello_project}" -v:minimal
elif [[ ! -x "${hello_output}/${apphost_name}" ]]; then
  echo "Expected prebuilt Hello apphost at ${hello_output}/${apphost_name}." >&2
  exit 1
fi

launch_args=("$@")
if [[ "${#launch_args[@]}" == "0" ]] &&
   [[ "${PROGPU_WPF_HELLO_RUN_VALIDATE:-0}" == "1" || "${PROGPU_WPF_HELLO_LIVE_VALIDATE:-0}" == "1" ]]; then
  launch_args=("hello-alpha" "hello beta")
fi

if [[ "${PROGPU_WPF_HELLO_LIVE_VALIDATE:-0}" == "1" ]]; then
  live_log="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-hello-live.XXXXXX")"
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
    rm -f "${live_log}"
  }
  trap cleanup_live_probe EXIT

  echo "Launching ProGPU WPF Hello apphost live geometry probe..."
  (
    cd "${hello_output}"
    if (("${#launch_args[@]}" > 0)); then
      "./${apphost_name}" "${launch_args[@]}"
    else
      "./${apphost_name}"
    fi
  ) >"${live_log}" 2>&1 &
  apphost_pid="$!"

  live_validation_line=""
  for _ in {1..600}; do
    live_validation_line="$(grep -E "ProGPU WPF HelloApp live input validation succeeded:" "${live_log}" | tail -n 1 || true)"
    if [[ -n "${live_validation_line}" ]]; then
      break
    fi

    if ! kill -0 "${apphost_pid}" 2>/dev/null; then
      live_validation_line="$(grep -E "ProGPU WPF HelloApp live input validation succeeded:" "${live_log}" | tail -n 1 || true)"
      if [[ -n "${live_validation_line}" ]]; then
        break
      fi

      echo "Hello apphost exited before live input validation succeeded." >&2
      cat "${live_log}" >&2
      exit 1
    fi

    sleep 0.05
  done

  if [[ -z "${live_validation_line}" ]]; then
    echo "Expected Hello apphost live input validation to succeed before timeout." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  logical_width=520
  logical_height=360
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
      echo "Expected Hello apphost viewport to cover full pixel target, but got viewport ${viewport_width}x${viewport_height}@${viewport_x},${viewport_y} for ${pixel_width}x${pixel_height} pixels." >&2
      cat "${live_log}" >&2
      exit 1
    fi
  else
    echo "Could not parse Hello apphost geometry from live validation line." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if (( pixel_width < logical_width || pixel_height < logical_height )); then
    echo "Expected Hello apphost pixels to cover ${logical_width}x${logical_height} logical content, but got ${pixel_width}x${pixel_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  trap - EXIT
  cleanup_live_probe >/dev/null 2>&1
  echo "${live_validation_line}"
  echo "ProGPU WPF HelloApp live geometry validation succeeded: logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}."
  exit 0
fi

echo "Launching ProGPU WPF Hello apphost..."
(
  cd "${hello_output}"
  if (("${#launch_args[@]}" > 0)); then
    "./${apphost_name}" "${launch_args[@]}"
  else
    "./${apphost_name}"
  fi
)
