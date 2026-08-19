#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.42}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.53}"
transport_target_framework="${PROGPU_WPF_TRANSPORT_TARGET_FRAMEWORK:-net10.0}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_path() {
  local package_id="$1"
  echo "${package_output}/$(progpu_preview_package_file_name "${package_id}")"
}

package_assembly_name() {
  local package_id="$1"
  case "${package_id}" in
    LibreWPF.Interop)
      echo "ProGPU.Wpf.Interop"
      ;;
    LibreWPF.ProGPU)
      echo "ProGPU.Wpf"
      ;;
    ProGPU.System.Drawing.Common)
      echo "System.Drawing.Common"
      ;;
    ProGPU.SkiaSharp)
      echo "SkiaSharp"
      ;;
    *)
      echo "${package_id}"
      ;;
  esac
}

is_expected_package_artifact() {
  local file_name="$1"
  local package_id
  for package_id in "${all_packages[@]}"; do
    if [[ "${file_name}" == "$(progpu_preview_package_file_name "${package_id}")" ]]; then
      return 0
    fi
  done

  return 1
}

require_package() {
  local package_id="$1"
  local package_file
  package_file="$(package_path "${package_id}")"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
}

require_entry() {
  local package_id="$1"
  local entry="$2"
  local package_file
  local entries
  package_file="$(package_path "${package_id}")"
  entries="$(unzip -Z -1 "${package_file}")"
  if ! grep -Fxq "${entry}" <<<"${entries}"; then
    echo "Package ${package_id} is missing '${entry}'." >&2
    exit 1
  fi
}

require_entry_contains() {
  local package_id="$1"
  local entry="$2"
  local expected="$3"
  local package_file
  package_file="$(package_path "${package_id}")"
  if ! unzip -p "${package_file}" "${entry}" | grep -Fq "${expected}"; then
    echo "Package ${package_id} entry '${entry}' is missing '${expected}'." >&2
    exit 1
  fi
}

sha256_stdin() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print $1}'
  else
    shasum -a 256 | awk '{print $1}'
  fi
}

require_entry_sha256() {
  local package_id="$1"
  local entry="$2"
  local expected="$3"
  local package_file
  local actual
  package_file="$(package_path "${package_id}")"
  actual="$(unzip -p "${package_file}" "${entry}" | sha256_stdin)"
  if [[ "${actual}" != "${expected}" ]]; then
    echo "Package ${package_id} entry '${entry}' has SHA-256 ${actual}, expected ${expected}." >&2
    exit 1
  fi
}

reject_entry() {
  local package_id="$1"
  local entry="$2"
  local package_file
  local entries
  package_file="$(package_path "${package_id}")"
  entries="$(unzip -Z -1 "${package_file}")"
  if grep -Fxq "${entry}" <<<"${entries}"; then
    echo "Package ${package_id} should not contain '${entry}'." >&2
    exit 1
  fi
}

require_nuspec_contains() {
  local package_id="$1"
  local expected="$2"
  local package_file
  package_file="$(package_path "${package_id}")"
  if ! unzip -p "${package_file}" "${package_id}.nuspec" | grep -Fq "${expected}"; then
    echo "Package ${package_id} nuspec is missing '${expected}'." >&2
    exit 1
  fi
}

require_nuspec_repository() {
  local package_id="$1"
  local expected_url="$2"
  local expected_commit="$3"
  local package_file
  package_file="$(package_path "${package_id}")"

  if ! unzip -p "${package_file}" "${package_id}.nuspec" | node \
    "${repo_root}/eng/progpu-nuspec-repository-audit.mjs" \
    "${package_id}" \
    "${expected_url}" \
    "${expected_commit}"; then
    exit 1
  fi
}

runtime_packages=("${progpu_preview_runtime_package_ids[@]}")
all_packages=("${progpu_preview_package_ids[@]}")
wpf_repository_packages=(LibreWPF.Transport LibreWPF.ProGPU LibreWPF.Sdk)
wpf_commit="$(git -C "${repo_root}" rev-parse --verify HEAD)"
progpu_commit="$(git -C "${repo_root}/external/ProGPU" rev-parse --verify HEAD)"
node "${repo_root}/eng/progpu-nuspec-repository-audit.mjs" --self-test

unexpected_package_found=0
while IFS= read -r -d '' artifact; do
  file_name="$(basename "${artifact}")"
  if ! is_expected_package_artifact "${file_name}"; then
    echo "Unexpected preview package artifact in output: ${artifact}" >&2
    unexpected_package_found=1
  fi
done < <(find "${package_output}" -maxdepth 1 -type f \( -name "*.nupkg" -o -name "*.snupkg" \) -print0)

if [[ "${unexpected_package_found}" -ne 0 ]]; then
  exit 1
fi

for package_id in "${all_packages[@]}"; do
  require_package "${package_id}"
  require_entry "${package_id}" "README.md"
  require_nuspec_contains "${package_id}" "<readme>README.md</readme>"
done

for package_id in "${runtime_packages[@]}"; do
  require_entry "${package_id}" "lib/net10.0/$(package_assembly_name "${package_id}").dll"
  require_nuspec_repository \
    "${package_id}" \
    "https://github.com/wieslawsoltes/ProGPU" \
    "${progpu_commit}"
done

for package_id in "${wpf_repository_packages[@]}"; do
  require_nuspec_repository \
    "${package_id}" \
    "https://github.com/wieslawsoltes/wpf" \
    "${wpf_commit}"
done

require_entry LibreWPF.ProGPU "lib/net10.0/ProGPU.Wpf.dll"
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.Backend\" version=\"${progpu_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.DirectX\" version=\"${progpu_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.Scene\" version=\"${progpu_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"LibreWPF.Interop\" version=\"${progpu_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.Input\" version=\"2.23.0\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.Windowing\" version=\"2.23.0\""

require_nuspec_contains ProGPU.Text "dependency id=\"ProGPU.Text.Shaping\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.WinUI "dependency id=\"ProGPU.Media\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.WinUI "dependency id=\"ProGPU.Media.Scene\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.WinUI "dependency id=\"ProGPU.WinRT\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Backend.Dawn\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Backend\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Layout\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Scene\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.WinRT\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.WinUI\" version=\"${progpu_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Avalonia\" version=\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""

require_nuspec_contains LibreWPF.Sdk "<packageType name=\"MSBuildSdk\" />"
require_entry LibreWPF.Sdk "Sdk/Sdk.props"
require_entry LibreWPF.Sdk "Sdk/LibreWPF.Sdk.Version.props"
require_entry_contains LibreWPF.Sdk "Sdk/LibreWPF.Sdk.Version.props" "<_LibreWpfSdkPackageVersion>${dev_package_version}</_LibreWpfSdkPackageVersion>"
require_entry LibreWPF.Sdk "Sdk/Sdk.targets"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.props"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.targets"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.Win32Compat.c"

transport_entries=(
  "lib/${transport_target_framework}/WindowsBase.dll"
  "lib/${transport_target_framework}/Microsoft.Win32.SystemEvents.dll"
  "lib/${transport_target_framework}/PresentationCore.dll"
  "lib/${transport_target_framework}/PresentationFramework.dll"
  "lib/${transport_target_framework}/PresentationFramework.Fluent.dll"
  "lib/${transport_target_framework}/System.Xaml.dll"
  "lib/${transport_target_framework}/System.Windows.Controls.Ribbon.dll"
  "lib/${transport_target_framework}/System.Windows.Presentation.dll"
  "lib/${transport_target_framework}/Accessibility.dll"
  "lib/${transport_target_framework}/System.Printing.dll"
  "lib/${transport_target_framework}/UIAutomationTypes.dll"
  "lib/${transport_target_framework}/System.Private.Windows.Core.dll"
  "ref/${transport_target_framework}/WindowsBase.dll"
  "ref/${transport_target_framework}/Microsoft.Win32.SystemEvents.dll"
  "ref/${transport_target_framework}/PresentationCore.dll"
  "ref/${transport_target_framework}/PresentationFramework.dll"
  "ref/${transport_target_framework}/PresentationFramework.Fluent.dll"
  "ref/${transport_target_framework}/System.Xaml.dll"
  "ref/${transport_target_framework}/System.Windows.Controls.Ribbon.dll"
  "ref/${transport_target_framework}/System.Windows.Presentation.dll"
  "ref/${transport_target_framework}/System.Printing.dll"
  "ref/${transport_target_framework}/UIAutomationTypes.dll"
  "buildTransitive/LibreWPF.Transport.targets"
  "buildTransitive/assets/LibreWPF/Fonts/LibreWPF.FluentSymbols.ttf"
  "notices/LibreWPF.FluentSymbols/NOTICE.md"
  "notices/LibreWPF.FluentSymbols/SOURCE-MANIFEST.json"
  "notices/LibreWPF.FluentSymbols/LegacyFluentGlyphMap.json"
  "notices/LibreWPF.FluentSymbols/licenses/Uno.Fonts-APACHE-2.0.txt"
  "notices/LibreWPF.FluentSymbols/licenses/FluentSystemIcons-MIT.txt"
  "notices/LibreWPF.FluentSymbols/licenses/WPF-Samples-MIT.txt"
  "notices/Microsoft.WindowsDesktop.App.Runtime/LICENSE"
  "runtimes/win-x86/native/PresentationNative_cor3.dll"
  "runtimes/win-x64/native/PresentationNative_cor3.dll"
  "runtimes/win-arm64/native/PresentationNative_cor3.dll"
  "runtimes/win-x86/native/wpfgfx_cor3.dll"
  "runtimes/win-x64/native/wpfgfx_cor3.dll"
  "runtimes/win-arm64/native/wpfgfx_cor3.dll"
  "runtimes/win-x86/native/ijwhost.dll"
  "runtimes/win-x64/native/ijwhost.dll"
  "runtimes/win-arm64/native/ijwhost.dll"
  "runtimes/win-x86/lib/${transport_target_framework}/PresentationCore.dll"
  "runtimes/win-x64/lib/${transport_target_framework}/PresentationCore.dll"
  "runtimes/win-arm64/lib/${transport_target_framework}/PresentationCore.dll"
  "runtimes/win-x86/lib/${transport_target_framework}/DirectWriteForwarder.dll"
  "runtimes/win-x64/lib/${transport_target_framework}/DirectWriteForwarder.dll"
  "runtimes/win-arm64/lib/${transport_target_framework}/DirectWriteForwarder.dll"
)

for entry in "${transport_entries[@]}"; do
  require_entry LibreWPF.Transport "${entry}"
done

windows_managed_payload_dir="${LIBREWPF_WINDOWS_MANAGED_PAYLOAD_DIR:-${repo_root}/artifacts/windows-managed-runtime}"
for rid in win-x86 win-x64 win-arm64; do
  windows_presentation_core="${windows_managed_payload_dir}/${rid}/${transport_target_framework}/PresentationCore.dll"
  if [[ ! -f "${windows_presentation_core}" ]]; then
    echo "Windows-built PresentationCore payload is missing at ${windows_presentation_core}." >&2
    exit 1
  fi
  windows_presentation_core_hash="$(sha256_stdin < "${windows_presentation_core}")"
  require_entry_sha256 LibreWPF.Transport \
    "runtimes/${rid}/lib/${transport_target_framework}/PresentationCore.dll" \
    "${windows_presentation_core_hash}"

  windows_direct_write_forwarder="${windows_managed_payload_dir}/${rid}/${transport_target_framework}/DirectWriteForwarder.dll"
  if [[ ! -f "${windows_direct_write_forwarder}" ]]; then
    echo "Windows-built DirectWriteForwarder payload is missing at ${windows_direct_write_forwarder}." >&2
    exit 1
  fi
  windows_direct_write_forwarder_hash="$(sha256_stdin < "${windows_direct_write_forwarder}")"
  require_entry_sha256 LibreWPF.Transport \
    "runtimes/${rid}/lib/${transport_target_framework}/DirectWriteForwarder.dll" \
    "${windows_direct_write_forwarder_hash}"

  windows_ijw_host="${windows_managed_payload_dir}/${rid}/native/ijwhost.dll"
  if [[ ! -f "${windows_ijw_host}" ]]; then
    echo "Windows IJW host payload is missing at ${windows_ijw_host}." >&2
    exit 1
  fi
  windows_ijw_host_hash="$(sha256_stdin < "${windows_ijw_host}")"
  require_entry_sha256 LibreWPF.Transport \
    "runtimes/${rid}/native/ijwhost.dll" \
    "${windows_ijw_host_hash}"
done

symbol_manifest_entry="notices/LibreWPF.FluentSymbols/SOURCE-MANIFEST.json"
symbol_manifest_json="$(unzip -p "$(package_path LibreWPF.Transport)" "${symbol_manifest_entry}")"
symbol_font_hash="$(node -e 'const value = JSON.parse(process.argv[1]); process.stdout.write(value.generatedFont.sha256);' "${symbol_manifest_json}")"
symbol_mapping_path="$(node -e 'const value = JSON.parse(process.argv[1]); process.stdout.write(value.mapping.path);' "${symbol_manifest_json}")"
symbol_mapping_hash="$(node -e 'const value = JSON.parse(process.argv[1]); process.stdout.write(value.mapping.sha256);' "${symbol_manifest_json}")"
symbol_notice_path="$(node -e 'const value = JSON.parse(process.argv[1]); process.stdout.write(value.notice.path);' "${symbol_manifest_json}")"
symbol_notice_hash="$(node -e 'const value = JSON.parse(process.argv[1]); process.stdout.write(value.notice.sha256);' "${symbol_manifest_json}")"
require_entry_sha256 LibreWPF.Transport "buildTransitive/assets/LibreWPF/Fonts/LibreWPF.FluentSymbols.ttf" "${symbol_font_hash}"
require_entry_sha256 LibreWPF.Transport "notices/LibreWPF.FluentSymbols/${symbol_mapping_path}" "${symbol_mapping_hash}"
require_entry_contains LibreWPF.Transport "buildTransitive/LibreWPF.Transport.targets" "LibreWPF.FluentSymbols/LegacyFluentGlyphMap.json"
require_entry_sha256 LibreWPF.Transport "notices/LibreWPF.FluentSymbols/${symbol_notice_path}" "${symbol_notice_hash}"
while IFS='|' read -r license_path license_hash; do
  require_entry_sha256 LibreWPF.Transport "notices/LibreWPF.FluentSymbols/${license_path}" "${license_hash}"
done < <(node -e '
  const value = JSON.parse(process.argv[1]);
  for (const source of value.sources) {
    process.stdout.write(`${source.licenseFile}|${source.licenseSha256}\n`);
  }
' "${symbol_manifest_json}")

reject_entry LibreWPF.Transport "lib/${transport_target_framework}/WindowsFormsIntegration.dll"
reject_entry LibreWPF.Transport "ref/${transport_target_framework}/WindowsFormsIntegration.dll"
reject_entry LibreWPF.Transport "ref/${transport_target_framework}/Accessibility.dll"
reject_entry LibreWPF.Transport "runtime.json"
if unzip -Z -1 "$(package_path LibreWPF.Transport)" \
    | grep -E '^(lib|ref)/' \
    | grep -Ev "^(lib|ref)/${transport_target_framework}/" \
    | grep -q .; then
  echo "Package LibreWPF.Transport contains payload for a target framework other than ${transport_target_framework}." >&2
  unzip -Z -1 "$(package_path LibreWPF.Transport)" \
    | grep -E '^(lib|ref)/' \
    | grep -Ev "^(lib|ref)/${transport_target_framework}/" >&2
  exit 1
fi
require_nuspec_contains LibreWPF.Transport "<group targetFramework=\"${transport_target_framework}\" />"

echo "LibreWPF preview package audit succeeded for ${dev_package_version}."
