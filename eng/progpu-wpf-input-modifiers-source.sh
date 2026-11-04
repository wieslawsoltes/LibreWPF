#!/usr/bin/env bash
set -euo pipefail

# Reuse the source framework assembly from the MessageBox gate and build only
# the additional device test project. These tests need no native window or GPU.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_INPUT_MODIFIERS_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_INPUT_MODIFIERS_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_INPUT_MODIFIERS_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
framework_assembly="${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll"
if [[ ! -f "${framework_assembly}" ]]; then
  echo "Build the original PresentationFramework.Tests source project before testing input modifiers." >&2
  exit 1
fi

export LIBREWPF_TEST_MEDIA_BACKEND=Portable
"${dotnet_command}" "${framework_assembly}" \
  --filter-class System.Windows.PortableWindowActivationServiceTests \
  --filter-method '*PointerModifier*' \
  --filter-method '*PortableDeviceStateAndCommittedTextHaveOneOwnerOnEveryOs' \
  --minimum-expected-tests 3 --fail-skips on --timeout 60s --no-progress

"${dotnet_command}" build \
  "${repo_root}/src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj" \
  --configuration "${configuration}" -m:1 -p:UseSharedCompilation=false --verbosity minimal
"${dotnet_command}" \
  "${repo_root}/artifacts/bin/PresentationCore.Tests/${configuration}/net10.0-windows/PresentationCore.Tests.dll" \
  --filter-class System.Windows.Input.PortableInputOwnershipTests \
  --filter-method '*EventModifier*' \
  --minimum-expected-tests 7 --fail-skips on --timeout 60s --no-progress
