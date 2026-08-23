#!/usr/bin/env bash
set -euo pipefail

# Opens several top-level ProGPU WPF windows on a headless X server using Mesa
# software rendering - the environment reported in LibreWPF issue #102, where the
# second window aborted the process inside wgpu's GLES/EGL backend. The abort
# crosses the native boundary and cannot be caught in managed code, so the test is
# simply that the harness runs to completion.
#
# This runs against whatever adapter wgpu selects. Forcing its GLES/EGL backend
# still cannot keep several windows alive in one process - see
# docs/progpu-wpf-multi-window-render-device.md.

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

if ! command -v Xvfb >/dev/null 2>&1; then
  echo "Required Linux multi-window smoke dependency 'Xvfb' is unavailable." >&2
  exit 1
fi

harness_project="${repo_root}/src/ProGPU.Wpf.MultiWindowSmokeHarness/ProGPU.Wpf.MultiWindowSmokeHarness.csproj"
harness_output="${repo_root}/artifacts/progpu-wpf-multi-window-smoke"
runtime_identifier="${PROGPU_WPF_MULTI_WINDOW_SMOKE_RID:-linux-x64}"

echo "Publishing ProGPU WPF multi-window smoke harness (${runtime_identifier})..."
rm -rf "${harness_output}"
# A runtime-identifier publish puts libglfw/libwgpu_native next to the harness.
# Framework-dependent output leaves them under runtimes/<rid>/native, where Silk's
# native loader does not find them on this lane.
"${dotnet}" publish "${harness_project}" \
  -c "${PROGPU_WPF_MULTI_WINDOW_SMOKE_CONFIGURATION:-Release}" \
  -r "${runtime_identifier}" \
  --self-contained false \
  -o "${harness_output}" \
  -v:minimal

smoke_log="$(mktemp "${TMPDIR:-/tmp}/librewpf-linux-multi-window.XXXXXX")"
xvfb_pid=""
cleanup() {
  status=$?
  if [[ -n "${xvfb_pid}" ]] && kill -0 "${xvfb_pid}" 2>/dev/null; then
    kill "${xvfb_pid}" 2>/dev/null || true
  fi
  if ((status != 0)); then
    echo "LibreWPF Linux multi-window smoke log:" >&2
    cat "${smoke_log}" >&2 || true
  fi
  rm -f "${smoke_log}"
}
trap cleanup EXIT

display="${PROGPU_WPF_MULTI_WINDOW_SMOKE_DISPLAY:-:99}"
Xvfb "${display}" -screen 0 1920x1080x24 +extension RANDR +extension GLX +extension XTEST \
  >"${smoke_log}" 2>&1 &
xvfb_pid=$!

for _ in $(seq 1 100); do
  if [[ -e "/tmp/.X11-unix/X${display#:}" ]]; then
    break
  fi
  if ! kill -0 "${xvfb_pid}" 2>/dev/null; then
    echo "Xvfb exited before the multi-window smoke display was ready." >&2
    exit 1
  fi
  sleep 0.1
done

export DISPLAY="${display}"
# GLFW probes Wayland before X11 and complains when XDG_RUNTIME_DIR is unset.
runtime_dir="${XDG_RUNTIME_DIR:-}"
if [[ -z "${runtime_dir}" ]]; then
  runtime_dir="$(mktemp -d "${TMPDIR:-/tmp}/librewpf-xdg-runtime.XXXXXX")"
  chmod 700 "${runtime_dir}"
  export XDG_RUNTIME_DIR="${runtime_dir}"
fi

# Mesa software rendering, exactly as reported: llvmpipe through the GL/EGL lane.
export LIBGL_ALWAYS_SOFTWARE="${LIBGL_ALWAYS_SOFTWARE:-1}"
export GALLIUM_DRIVER="${GALLIUM_DRIVER:-llvmpipe}"

echo "Running ProGPU WPF multi-window smoke on ${display}..."
set +e
"${dotnet}" "${harness_output}/ProGPU.Wpf.MultiWindowSmokeHarness.dll" >>"${smoke_log}" 2>&1
harness_status=$?
set -e

grep -E "^(ProGPU WPF multi-window smoke|')" "${smoke_log}" || true

if ((harness_status != 0)); then
  echo "ProGPU WPF multi-window smoke failed with exit code ${harness_status}." >&2
  exit "${harness_status}"
fi

echo "ProGPU WPF Linux multi-window smoke succeeded."
