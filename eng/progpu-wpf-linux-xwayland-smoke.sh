#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

for command in xvfb-run xdotool; do
  if ! command -v "${command}" >/dev/null 2>&1; then
    echo "Required Linux windowing smoke dependency '${command}' is unavailable." >&2
    exit 1
  fi
done

# The repository's WPF Arcade targets intentionally build the local
# PresentationBuildTasks project when a source-tree sample compiles XAML.
# Restore that project explicitly because the Linux job consumes the package
# artifact produced by another job and therefore has not run the source build
# that normally creates its project.assets.json.
dotnet restore \
  "${repo_root}/src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj" \
  -p:TargetFramework=net10.0 \
  -v:minimal

smoke_log="$(mktemp "${TMPDIR:-/tmp}/librewpf-linux-xwayland.XXXXXX")"
native_drag_status="$(mktemp "${TMPDIR:-/tmp}/librewpf-linux-native-drag.XXXXXX")"
cleanup() {
  status=$?
  if ((status != 0)); then
    echo "LibreWPF Linux Wayland-session/XWayland smoke log:" >&2
    cat "${smoke_log}" >&2 || true
  fi
  rm -f "${smoke_log}" "${native_drag_status}"
}
trap cleanup EXIT

xvfb-run -a --server-args="-screen 0 1280x1024x24" bash -c '
  set -euo pipefail

  export XDG_SESSION_TYPE=wayland
  export WAYLAND_DISPLAY=wayland-ci
  unset PROGPU_WPF_LINUX_WINDOWING

  PROGPU_WPF_SHOWCASE_REBUILD_PACKAGES=0 \
  PROGPU_WPF_SHOWCASE_VALIDATE=0 \
  PROGPU_WPF_SHOWCASE_RUN_VALIDATE=0 \
  PROGPU_WPF_SHOWCASE_LIVE_VALIDATE=1 \
  PROGPU_WPF_SHOWCASE_LIVE_VALIDATE_TIMEOUT_SECONDS=90 \
  PROGPU_WPF_SHOWCASE_NATIVE_DRAG_STATUS_PATH="$3" \
    "$1/eng/run-progpu-wpf-showcase.sh" >"$2" 2>&1 &
  probe_pid=$!

  cleanup_probe() {
    if kill -0 "${probe_pid}" 2>/dev/null; then
      kill "${probe_pid}" 2>/dev/null || true
      wait "${probe_pid}" 2>/dev/null || true
    fi
  }
  trap cleanup_probe EXIT

  window_id=""
  for _ in $(seq 1 600); do
    if ! kill -0 "${probe_pid}" 2>/dev/null; then
      wait "${probe_pid}"
      echo "LibreWPF Showcase probe exited before its X11 window became visible." >&2
      exit 1
    fi

    window_id="$(xdotool search --onlyvisible --name "ProGPU WPF Showcase" 2>/dev/null | head -n 1 || true)"
    if [[ -n "${window_id}" ]]; then
      break
    fi

    sleep 0.25
  done

  if [[ -z "${window_id}" ]]; then
    echo "Could not locate the live LibreWPF Showcase X11 window before timeout." >&2
    exit 1
  fi

  native_drag_ready=0
  for _ in $(seq 1 600); do
    if ! kill -0 "${probe_pid}" 2>/dev/null; then
      wait "${probe_pid}"
      echo "LibreWPF Showcase probe exited before requesting the external native drag." >&2
      exit 1
    fi

    if [[ -f "$3" ]] && [[ "$(cat "$3")" == "ready" ]]; then
      native_drag_ready=1
      break
    fi

    sleep 0.05
  done

  if ((native_drag_ready == 0)); then
    echo "LibreWPF Showcase probe did not request the external native drag before timeout." >&2
    exit 1
  fi

  xdotool mousemove --window "${window_id}" 360 300
  xdotool mousedown 1
  for step in $(seq 1 36); do
    xdotool mousemove --window "${window_id}" "$((360 + step * 3))" "$((300 + step))"
  done
  xdotool mouseup 1
  printf "completed" >"$3"

  wait "${probe_pid}"
  trap - EXIT
' bash "${repo_root}" "${smoke_log}" "${native_drag_status}"

grep -F "ProGPU WPF Showcase live input validation succeeded:" "${smoke_log}"
grep -F "external 36-step native drag returned to dispatcher processing" "${smoke_log}"
grep -F "windowing backend X11, wayland session True, global position True, interactive move True, native popups True, owner-composited popups False" "${smoke_log}"
grep -F "Menu, ComboBox dropdown, and direct Popup opened through ProGPU popup surfaces" "${smoke_log}"
grep -F "native windows 1/1/1" "${smoke_log}"
grep -F "runtime framework themes switched and rendered native menu popups: Aero, Aero2, AeroLite, Classic, Fluent, Luna, Royale" "${smoke_log}"

echo "LibreWPF Linux Wayland-session/XWayland native drag, dispatcher, popup, and theme smoke succeeded."
