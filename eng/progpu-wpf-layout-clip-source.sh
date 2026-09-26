#!/usr/bin/env bash
set -euo pipefail

# The preceding source MessageBox gate builds this original test project and
# its source dependency graph. Reuse that output, not another WPF ABI or package
# fixture. This checks typed clip producers, not rendered idle application cost.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_LAYOUT_CLIP_SOURCE_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" ]]; then
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
assembly="${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationFramework.Tests source project before running layout clip producer contracts: ${assembly}" >&2
  exit 1
fi

export LIBREWPF_TEST_MEDIA_BACKEND=Portable
exec "${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.PortableLayoutClipSourceTests \
  --minimum-expected-tests 16 --fail-skips on --timeout 60s --no-progress
