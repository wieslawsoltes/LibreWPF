#!/usr/bin/env bash
set -euo pipefail

# Reuse the original source test assembly built by the preceding MessageBox
# gate. These are real WPF controls with a typed recording popup host, not
# native window activation, visual, placement, or capture qualification.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -n "${PROGPU_WPF_POPUP_SOURCE_DOTNET+set}" ]]; then
  dotnet_command="${PROGPU_WPF_POPUP_SOURCE_DOTNET}"
  if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
    echo "PROGPU_WPF_POPUP_SOURCE_DOTNET must name an executable file: ${dotnet_command}" >&2
    exit 2
  fi
elif [[ -x "${repo_root}/.dotnet/dotnet" ]]; then
  dotnet_command="${repo_root}/.dotnet/dotnet"
else
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
assembly="${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationFramework.Tests source project before running popup dismissal contracts: ${assembly}" >&2
  exit 1
fi

export LIBREWPF_TEST_MEDIA_BACKEND=Portable
exec "${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.PortablePopupOwnershipTests \
  --filter-method '*OwnerDeactivation*' \
  --minimum-expected-tests 12 --fail-skips on --timeout 60s --no-progress
