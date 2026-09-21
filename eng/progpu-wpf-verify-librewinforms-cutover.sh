#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sdk_targets="${repo_root}/packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.targets"
sdk_ci="${repo_root}/eng/progpu-wpf-sdk-ci.sh"

require_text() {
  local file="$1"
  local expected="$2"
  if ! grep -Fq -- "${expected}" "${file}"; then
    echo "Missing '${expected}' in ${file}." >&2
    exit 1
  fi
}

require_text "${sdk_targets}" '<ProGpuWpfUseCanonicalLibreWinForms Condition='
require_text "${sdk_targets}" '>LibreWinForms.System.Windows.Forms</ProGpuWpfLibreWinFormsRuntimePackageId>'
require_text "${sdk_targets}" '<PackageReference Include="LibreWinForms.ProGPU"'
require_text "${sdk_targets}" '<PackageReference Include="LibreWinForms.WindowsFormsIntegration"'
require_text "${sdk_ci}" 'PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_DIR'
require_text "${sdk_ci}" 'ProGpuWpfUseCanonicalLibreWinForms=true'
require_text "${sdk_ci}" 'LibreWinForms.System.Windows.Forms'

if grep -Fq -- 'external/LibreWinForms/src/LibreWinForms.Portable/' "${sdk_ci}"; then
  echo "LibreWPF SDK CI must not restore the retired LibreWinForms.Portable source lane." >&2
  exit 1
fi

echo "LibreWPF canonical LibreWinForms package cutover verification succeeded."
