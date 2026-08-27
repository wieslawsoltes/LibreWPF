#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

configuration="${PROGPU_WPF_NATIVE_MIL_HOST_CONFIGURATION:-Release}"
host_project="${repo_root}/src/ProGPU.Wpf.RealPresentationFrameworkHarness/ProGPU.Wpf.RealPresentationFrameworkHarness.csproj"
host_dll="${repo_root}/artifacts/bin/ProGPU.Wpf.RealPresentationFrameworkHarness/${configuration}/net10.0/ProGPU.Wpf.RealPresentationFrameworkHarness.dll"

platform="$(uname -s 2>/dev/null || echo unknown)"
architecture="$(uname -m 2>/dev/null || echo unknown)"
case "${platform}" in
  Darwin)
    native_build_dir="${PROGPU_NATIVE_BUILD_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/build}"
    native_runtime_dir="${PROGPU_NATIVE_RUNTIME_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/runtime}"
    native_library="${native_build_dir}/libprogpu_native.dylib"
    ;;
  Linux)
    native_build_dir="${PROGPU_NATIVE_BUILD_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/build}"
    native_runtime_dir="${PROGPU_NATIVE_RUNTIME_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/runtime}"
    native_library="${native_build_dir}/libprogpu_native.so"
    if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
      echo "The native MIL host smoke requires an X11 or Wayland display." >&2
      exit 1
    fi
    ;;
  MINGW*|MSYS*|CYGWIN*)
    case "${architecture}" in
      arm64|aarch64) native_rid="win-arm64" ;;
      *) native_rid="win-x64" ;;
    esac
    native_build_dir="${PROGPU_NATIVE_BUILD_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/build-${native_rid}}"
    native_runtime_dir="${PROGPU_NATIVE_RUNTIME_DIR:-${repo_root}/external/ProGPU/artifacts/progpu-native/runtime-${native_rid}}"
    native_library="${native_build_dir}/progpu_native.dll"
    ;;
  *)
    echo "Unsupported native MIL host platform '${platform}'." >&2
    exit 1
    ;;
esac

if [[ ! -f "${native_library}" ]]; then
  echo "Expected the current ProGPU native renderer at ${native_library}." >&2
  echo "Build ProGPU native first or set PROGPU_NATIVE_BUILD_DIR/PROGPU_NATIVE_RUNTIME_DIR." >&2
  exit 1
fi

if [[ "${PROGPU_WPF_NATIVE_MIL_HOST_SKIP_BUILD:-0}" != "1" ]]; then
  "${dotnet}" build "${host_project}" \
    --configuration "${configuration}" \
    -m:1 \
    -nr:false \
    -v:minimal
elif [[ ! -f "${host_dll}" ]]; then
  echo "Expected the prebuilt native MIL host harness at ${host_dll}." >&2
  exit 1
fi

echo "Running source-built LibreWPF native MIL host smoke on ${platform}/${architecture}..."
case "${platform}" in
  Darwin)
    DYLD_LIBRARY_PATH="${native_build_dir}:${native_runtime_dir}${DYLD_LIBRARY_PATH:+:${DYLD_LIBRARY_PATH}}" \
      "${dotnet}" "${host_dll}" --native-mil-host
    ;;
  Linux)
    LD_LIBRARY_PATH="${native_build_dir}:${native_runtime_dir}${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}" \
      "${dotnet}" "${host_dll}" --native-mil-host
    ;;
  *)
    PATH="${native_build_dir}:${native_runtime_dir}${PATH:+:${PATH}}" \
      "${dotnet}" "${host_dll}" --native-mil-host
    ;;
esac
