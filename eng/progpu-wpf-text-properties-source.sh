#!/usr/bin/env bash
set -euo pipefail

# The input-modifier source gate has already built the real Core test assembly.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_TEXT_PROPERTIES_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_TEXT_PROPERTIES_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_TEXT_PROPERTIES_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
assembly="${repo_root}/artifacts/bin/PresentationCore.Tests/${CONFIGURATION:-Release}/net10.0-windows/PresentationCore.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationCore.Tests source project before testing text properties." >&2
  exit 1
fi
export LIBREWPF_TEST_MEDIA_BACKEND=Portable
"${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.Media.PortableTextLineTests \
  --minimum-expected-tests 111 --fail-skips on --timeout 60s --no-progress
"${dotnet_command}" "${assembly}" \
  --filter-class MS.Internal.FontCache.Tests.PortableFontFamilyUriTests \
  --minimum-expected-tests 15 --fail-skips on --timeout 60s --no-progress
