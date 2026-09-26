#!/usr/bin/env bash
set -euo pipefail

# The Windows public MessageBox route remains user32. This gate exercises the
# actual non-Windows WPF dialog and source modality without claiming native chrome.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_MESSAGEBOX_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" ]]; then
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
project="${repo_root}/src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj"

mkdir -p "${repo_root}/artifacts/packages/${configuration}/NonShipping"
"${dotnet_command}" restore \
  "${repo_root}/src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj" \
  --disable-parallel --verbosity minimal
"${dotnet_command}" build "${project}" --configuration "${configuration}" \
  -m:1 -p:UseSharedCompilation=false --verbosity minimal

LIBREWPF_TEST_MEDIA_BACKEND=Portable "${dotnet_command}" \
  "${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll" \
  --filter-class System.Windows.PortableWindowActivationServiceTests \
  --filter-class System.Windows.PortableMessageBoxModalTests \
  --filter-method '*Dialog*' \
  --filter-method '*PortableWindowBackdropNeverTreatsItsHostHandleAsWpfHwnd*' \
  --minimum-expected-tests 8 --fail-skips on --timeout 60s --no-progress
