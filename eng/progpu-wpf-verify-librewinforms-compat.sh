#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sdk_targets="${repo_root}/packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.targets"

require_text() {
  local text="$1"
  if ! grep -Fq -- "${text}" "${sdk_targets}"; then
    echo "Missing '${text}' in ${sdk_targets}." >&2
    exit 1
  fi
}

require_text '<ProGpuWpfLibreWinFormsRuntimePackageId Condition="'\''$(ProGpuWpfLibreWinFormsRuntimePackageId)'\'' == '\'''\''">LibreWinForms.Compatibility.System.Windows.Forms</ProGpuWpfLibreWinFormsRuntimePackageId>'
require_text '<PackageReference Include="$(ProGpuWpfLibreWinFormsRuntimePackageId)" Version="$(ProGpuWpfLibreWinFormsPackageVersion)"'
require_text '<PackageReference Include="$(ProGpuWpfLibreWinFormsRuntimePackageId)" VersionOverride="$(ProGpuWpfLibreWinFormsPackageVersion)"'
require_text '$([System.String]::Copy('\''$(ProGpuWpfLibreWinFormsRuntimePackageId)'\'').ToLowerInvariant())'

if grep -Fq -- 'PackageReference Include="LibreWinForms.System.Windows.Forms"' "${sdk_targets}"; then
  echo "LibreWPF.Sdk must not claim the canonical LibreWinForms runtime package while its WindowsFormsIntegration bridge still targets the compatibility runtime." >&2
  exit 1
fi

if grep -Fq -- 'librewinforms.system.windows.forms/' "${sdk_targets}"; then
  echo "LibreWPF.Sdk still contains a hard-coded global-package-cache path for the canonical LibreWinForms runtime package." >&2
  exit 1
fi

dotnet_command=""
if [[ -x "${repo_root}/.dotnet/dotnet" ]]; then
  dotnet_command="${repo_root}/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q .; then
  dotnet_command="dotnet"
fi

if [[ -n "${dotnet_command}" ]]; then
  "${dotnet_command}" msbuild \
    "${repo_root}/eng/ProGPU.Wpf.LibreWinFormsCompatValidation.proj" \
    -target:ValidateLibreWinFormsCompatibilityPackage \
    -verbosity:minimal
fi

echo "LibreWPF transitional LibreWinForms compatibility-package verification succeeded."
