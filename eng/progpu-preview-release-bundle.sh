#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.42}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.49}"
sdk_sample_target_framework="${PROGPU_WPF_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0-windows}"
manifest_path="${PROGPU_WPF_PREVIEW_PACKAGE_MANIFEST:-${package_output}/librewpf-preview-packages-${dev_package_version}.json}"
bundle_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE:-${package_output}/librewpf-preview-${dev_package_version}.tar.gz}"
sidecar_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE_SHA256:-${bundle_output}.sha256}"
release_readme_path="${PROGPU_WPF_PREVIEW_RELEASE_README:-${package_output}/README.md}"
release_nuget_config_path="${PROGPU_WPF_PREVIEW_RELEASE_NUGET_CONFIG:-${package_output}/NuGet.config}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_ids=("${progpu_preview_package_ids[@]}")

file_sha256() {
  local file="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    sha256sum "${file}" | awk '{print $1}'
  fi
}

"${repo_root}/eng/progpu-preview-package-manifest.sh"

bundle_dir="$(dirname "${bundle_output}")"
sidecar_dir="$(dirname "${sidecar_output}")"
readme_dir="$(dirname "${release_readme_path}")"
nuget_config_dir="$(dirname "${release_nuget_config_path}")"
mkdir -p "${bundle_dir}" "${sidecar_dir}" "${readme_dir}" "${nuget_config_dir}"
rm -f "${bundle_output}" "${sidecar_output}"

cat >"${release_readme_path}" <<README
# LibreWPF Preview ${dev_package_version}

This preview bundle contains the package set for running WPF applications on the ProGPU/Silk.NET platform through the custom \`LibreWPF.Sdk\`.

## Contents

- \`librewpf-preview-packages-${dev_package_version}.json\` records the exact package list, source commits, package sizes, and SHA-256 hashes.
- \`LibreWPF.Transport.${dev_package_version}.nupkg\` contains the ported managed WPF transport assemblies.
- \`LibreWPF.Sdk.${dev_package_version}.nupkg\` is the custom MSBuild SDK package.
- \`LibreWPF.ProGPU.${dev_package_version}.nupkg\` contains the WPF bridge.
- \`LibreWPF.Interop.${progpu_package_version}.nupkg\` and the \`ProGPU.*.${progpu_package_version}.nupkg\` packages are the exact immutable ProGPU runtime dependencies.

Verify the archive with the adjacent checksum file:

\`\`\`bash
shasum -a 256 -c librewpf-preview-${dev_package_version}.tar.gz.sha256
\`\`\`

Use the extracted directory as a local NuGet source, or copy the bundled \`NuGet.config\` next to your solution and keep the extracted bundle beside it. Then switch an existing WPF project to the custom SDK:

\`\`\`xml
<Project Sdk="LibreWPF.Sdk/${dev_package_version}">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>${sdk_sample_target_framework}</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
\`\`\`

No ProGPU-specific source or XAML changes should be required for normal WPF application code. Windows-only interop remains the expected exception while the portable platform layer is still being completed.

For repository validation, run:

\`\`\`bash
./eng/progpu-preview-release-verify.sh
PROGPU_WPF_PREVIEW_RELEASE_REQUIRE_CLEAN_SOURCE=1 ./eng/progpu-preview-release-verify.sh
./eng/progpu-preview-release-sdk-smoke.sh
\`\`\`
README

cat >"${release_nuget_config_path}" <<NUGET
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="librewpf-preview" value="." />
    <add key="dotnet11" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json" />
    <add key="dotnet11-transport" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11-transport/nuget/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
NUGET

archive_entries=()
readme_name="$(basename "${release_readme_path}")"
nuget_config_name="$(basename "${release_nuget_config_path}")"
manifest_name="$(basename "${manifest_path}")"
archive_entries+=("${readme_name}")
archive_entries+=("${nuget_config_name}")
archive_entries+=("${manifest_name}")

if [[ ! -f "${manifest_path}" ]]; then
  echo "Missing preview package manifest ${manifest_path}." >&2
  exit 1
fi

if [[ ! -f "${release_readme_path}" ]]; then
  echo "Missing preview release README ${release_readme_path}." >&2
  exit 1
fi

if [[ ! -f "${release_nuget_config_path}" ]]; then
  echo "Missing preview release NuGet config ${release_nuget_config_path}." >&2
  exit 1
fi

for package_id in "${package_ids[@]}"; do
  package_name="$(progpu_preview_package_file_name "${package_id}")"
  package_file="${package_output}/${package_name}"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
  archive_entries+=("${package_name}")
done

(
  cd "${package_output}"
  COPYFILE_DISABLE=1 tar -czf "${bundle_output}" "${archive_entries[@]}"
)

expected_entries="$(printf '%s\n' "${archive_entries[@]}")"
actual_entries="$(tar -tzf "${bundle_output}")"
if [[ "${actual_entries}" != "${expected_entries}" ]]; then
  echo "Preview release bundle entries do not match the expected manifest/package set." >&2
  echo "Expected entries:" >&2
  printf '%s\n' "${archive_entries[@]}" >&2
  echo "Actual entries:" >&2
  tar -tzf "${bundle_output}" >&2
  exit 1
fi

bundle_sha256="$(file_sha256 "${bundle_output}")"
printf '%s  %s\n' "${bundle_sha256}" "$(basename "${bundle_output}")" >"${sidecar_output}"

echo "LibreWPF preview release bundle written to ${bundle_output}."
echo "LibreWPF preview release bundle SHA-256 written to ${sidecar_output}."
