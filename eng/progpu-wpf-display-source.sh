#!/usr/bin/env bash
set -euo pipefail

# Use the actual source-core test output built by the preceding input gate.
# No native runtime, renderer, device or standalone replacement harness is built.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_DISPLAY_SOURCE_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_DISPLAY_SOURCE_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_DISPLAY_SOURCE_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
assembly="${repo_root}/artifacts/bin/PresentationCore.Tests/${configuration}/net10.0-windows/PresentationCore.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationCore.Tests source project before running Display bridge contracts." >&2
  exit 1
fi
export LIBREWPF_TEST_MEDIA_BACKEND=Portable
exec "${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.Media.PortableDisplayTextSourceTests \
  --minimum-expected-tests 13 --fail-skips on --timeout 60s --no-progress
