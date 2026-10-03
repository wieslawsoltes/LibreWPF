#!/usr/bin/env bash
set -euo pipefail

# Reuse the original source test build produced by the earlier input gates.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_SHADER_SOURCE_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_SHADER_SOURCE_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_SHADER_SOURCE_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
assembly="${repo_root}/artifacts/bin/PresentationCore.Tests/${configuration}/net10.0-windows/PresentationCore.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build original source core test assemblies before testing ShaderEffect export." >&2
  exit 1
fi
LIBREWPF_TEST_MEDIA_BACKEND=Portable "${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.Media.Tests.PortableShaderEffectSourceTests \
  --minimum-expected-tests 14 --fail-skips on --timeout 60s --no-progress
