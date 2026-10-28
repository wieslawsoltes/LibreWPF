#!/usr/bin/env bash
set -euo pipefail

# Reuse the real Core test assembly already built by the input/CJK source gate.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_CATALOG_DESKTOP_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_CATALOG_DESKTOP_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_CATALOG_DESKTOP_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
assembly="${repo_root}/artifacts/bin/PresentationCore.Tests/${CONFIGURATION:-Release}/net10.0-windows/PresentationCore.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationCore.Tests source project before testing font discovery and desktop mapping." >&2
  exit 1
fi

export LIBREWPF_TEST_MEDIA_BACKEND=Portable
evidence="${repo_root}/artifacts/catalog-desktop-source"
for selection in \
  "PresentationCore.Tests.MS.Internal.Text.TextInterface.PortableSystemFontDiscoveryTests 4" \
  "PresentationCore.Tests.MS.Internal.Text.TextInterface.PortableFontDataTests 3" \
  "MS.Internal.Text.TextInterface.Tests.PortableFontCollectionTests 3" \
  "System.Windows.PortableDesktopTransformQueryTests 7"; do
  read -r test_class minimum_tests <<< "${selection}"
  "${dotnet_command}" "${assembly}" --filter-class "${test_class}" \
    --minimum-expected-tests "${minimum_tests}" --fail-skips on --timeout 60s --no-progress \
    --results-directory "${evidence}/${test_class}" --report-trx
done
"${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.PortablePresentationSourceTests --filter-method '*Desktop*' \
  --minimum-expected-tests 4 --fail-skips on --timeout 60s --no-progress \
  --results-directory "${evidence}/existing-desktop" --report-trx
"${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.PortablePresentationSourceTests --filter-method '*ClientOrigin*' \
  --minimum-expected-tests 3 --fail-skips on --timeout 60s --no-progress \
  --results-directory "${evidence}/existing-origin" --report-trx
