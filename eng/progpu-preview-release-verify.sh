#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.44}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.54}"
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

is_expected_release_artifact() {
  local file_name="$1"
  local package_id

  case "${file_name}" in
    "$(basename "${release_readme_path}")"|"$(basename "${release_nuget_config_path}")"|"$(basename "${manifest_path}")"|"$(basename "${bundle_output}")"|"$(basename "${sidecar_output}")")
      return 0
      ;;
  esac

  for package_id in "${package_ids[@]}"; do
    if [[ "${file_name}" == "$(progpu_preview_package_file_name "${package_id}")" ]]; then
      return 0
    fi
  done

  return 1
}

git_commit() {
  local git_root="$1"
  git -C "${git_root}" rev-parse --verify HEAD 2>/dev/null || printf 'unknown'
}

require_file() {
  local file="$1"
  if [[ ! -f "${file}" ]]; then
    echo "Missing preview release artifact ${file}." >&2
    exit 1
  fi
}

require_file "${bundle_output}"
require_file "${sidecar_output}"

unexpected_release_artifact_found=0
while IFS= read -r -d '' artifact; do
  file_name="$(basename "${artifact}")"
  if ! is_expected_release_artifact "${file_name}"; then
    echo "Unexpected preview release artifact in output: ${artifact}" >&2
    unexpected_release_artifact_found=1
  fi
done < <(find "${package_output}" -maxdepth 1 -type f \( -name "*.nupkg" -o -name "*.snupkg" -o -name "*.json" -o -name "*.tar.gz" -o -name "*.sha256" -o -name "README.md" -o -name "NuGet.config" \) -print0)

if [[ "${unexpected_release_artifact_found}" -ne 0 ]]; then
  exit 1
fi

bundle_sha256="$(file_sha256 "${bundle_output}")"
sidecar_sha256="$(awk '{print $1}' "${sidecar_output}")"
sidecar_file="$(awk '{print $2}' "${sidecar_output}")"
if [[ "${sidecar_sha256}" != "${bundle_sha256}" ]]; then
  echo "Preview release bundle checksum sidecar does not match ${bundle_output}." >&2
  exit 1
fi

if [[ "${sidecar_file}" != "$(basename "${bundle_output}")" ]]; then
  echo "Preview release bundle checksum sidecar references '${sidecar_file}' instead of '$(basename "${bundle_output}")'." >&2
  exit 1
fi

archive_entries=()
readme_name="$(basename "${release_readme_path}")"
nuget_config_name="$(basename "${release_nuget_config_path}")"
manifest_name="$(basename "${manifest_path}")"
archive_entries+=("${readme_name}")
archive_entries+=("${nuget_config_name}")
archive_entries+=("${manifest_name}")
for package_id in "${package_ids[@]}"; do
  archive_entries+=("$(progpu_preview_package_file_name "${package_id}")")
done

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

extract_dir="$(mktemp -d "${TMPDIR:-/tmp}/progpu-wpf-preview-release-verify.XXXXXX")"
trap 'rm -rf "${extract_dir}"' EXIT
tar -xzf "${bundle_output}" -C "${extract_dir}"

if [[ -f "${release_readme_path}" ]] && ! cmp -s "${release_readme_path}" "${extract_dir}/${readme_name}"; then
  echo "Preview release bundle README ${readme_name} does not match ${release_readme_path}." >&2
  exit 1
fi

if [[ -f "${release_nuget_config_path}" ]] && ! cmp -s "${release_nuget_config_path}" "${extract_dir}/${nuget_config_name}"; then
  echo "Preview release bundle NuGet config ${nuget_config_name} does not match ${release_nuget_config_path}." >&2
  exit 1
fi

if [[ -f "${manifest_path}" ]] && ! cmp -s "${manifest_path}" "${extract_dir}/${manifest_name}"; then
  echo "Preview release bundle manifest ${manifest_name} does not match ${manifest_path}." >&2
  exit 1
fi

readme_file="${extract_dir}/${readme_name}"
if ! grep -q "LibreWPF.Sdk/${dev_package_version}" "${readme_file}" \
  || ! grep -q "shasum -a 256 -c librewpf-preview-${dev_package_version}.tar.gz.sha256" "${readme_file}" \
  || ! grep -q "PROGPU_WPF_PREVIEW_RELEASE_REQUIRE_CLEAN_SOURCE=1 ./eng/progpu-preview-release-verify.sh" "${readme_file}" \
  || ! grep -q "No ProGPU-specific source or XAML changes should be required" "${readme_file}"; then
  echo "Preview release bundle README is missing required SDK switch or verification guidance." >&2
  exit 1
fi

nuget_config_file="${extract_dir}/${nuget_config_name}"
if ! grep -q "<add key=\"librewpf-preview\" value=\"\\.\" />" "${nuget_config_file}" \
  || ! grep -q "<add key=\"dotnet11\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json\" />" "${nuget_config_file}" \
  || ! grep -q "<add key=\"dotnet11-transport\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11-transport/nuget/v3/index.json\" />" "${nuget_config_file}" \
  || ! grep -q "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />" "${nuget_config_file}"; then
  echo "Preview release bundle NuGet config is missing required package sources." >&2
  exit 1
fi

export PROGPU_WPF_PREVIEW_RELEASE_CURRENT_WPF_COMMIT
export PROGPU_WPF_PREVIEW_RELEASE_CURRENT_PROGPU_COMMIT
export PROGPU_WPF_PREVIEW_RELEASE_CURRENT_LIBREWINFORMS_COMMIT
PROGPU_WPF_PREVIEW_RELEASE_CURRENT_WPF_COMMIT="$(git_commit "${repo_root}")"
PROGPU_WPF_PREVIEW_RELEASE_CURRENT_PROGPU_COMMIT="$(git_commit "${repo_root}/external/ProGPU")"
PROGPU_WPF_PREVIEW_RELEASE_CURRENT_LIBREWINFORMS_COMMIT="$(git_commit "${repo_root}/external/LibreWinForms")"

node - "${extract_dir}" "${manifest_name}" "${dev_package_version}" "${progpu_package_version}" "${package_ids[@]}" <<'NODE'
const fs = require("fs");
const crypto = require("crypto");
const path = require("path");

const [extractDirectory, manifestName, devPackageVersion, proGpuPackageVersion, ...packageIds] = process.argv.slice(2);
const manifestPath = path.join(extractDirectory, manifestName);
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));

function fail(message) {
  console.error(message);
  process.exit(1);
}

if (manifest.schemaVersion !== 4) {
  fail(`Expected preview manifest schemaVersion 4, found ${manifest.schemaVersion}.`);
}

if (manifest.version !== devPackageVersion) {
  fail(`Expected preview manifest version ${devPackageVersion}, found ${manifest.version}.`);
}

if (manifest.progpuVersion !== proGpuPackageVersion) {
  fail(`Expected preview manifest ProGPU version ${proGpuPackageVersion}, found ${manifest.progpuVersion}.`);
}

if (!manifest.source || !manifest.source.wpfCommit || !manifest.source.progpuCommit || !manifest.source.libreWinFormsCommit) {
  fail("Preview manifest source provenance is missing WPF, ProGPU, or LibreWinForms commit information.");
}

if (process.env.PROGPU_WPF_PREVIEW_RELEASE_REQUIRE_CLEAN_SOURCE === "1") {
  if (manifest.source.wpfIsDirty !== false || manifest.source.progpuIsDirty !== false || manifest.source.libreWinFormsIsDirty !== false) {
    fail("Preview manifest source provenance is dirty; regenerate the release bundle from clean WPF, ProGPU, and LibreWinForms worktrees.");
  }

  const expectedWpfCommit = process.env.PROGPU_WPF_PREVIEW_RELEASE_CURRENT_WPF_COMMIT;
  if (expectedWpfCommit && expectedWpfCommit !== "unknown" && manifest.source.wpfCommit !== expectedWpfCommit) {
    fail(`Preview manifest WPF commit ${manifest.source.wpfCommit} does not match current checkout ${expectedWpfCommit}.`);
  }

  const expectedProGpuCommit = process.env.PROGPU_WPF_PREVIEW_RELEASE_CURRENT_PROGPU_COMMIT;
  if (expectedProGpuCommit && expectedProGpuCommit !== "unknown" && manifest.source.progpuCommit !== expectedProGpuCommit) {
    fail(`Preview manifest ProGPU commit ${manifest.source.progpuCommit} does not match current checkout ${expectedProGpuCommit}.`);
  }

  const expectedLibreWinFormsCommit = process.env.PROGPU_WPF_PREVIEW_RELEASE_CURRENT_LIBREWINFORMS_COMMIT;
  if (expectedLibreWinFormsCommit && expectedLibreWinFormsCommit !== "unknown" && manifest.source.libreWinFormsCommit !== expectedLibreWinFormsCommit) {
    fail(`Preview manifest LibreWinForms commit ${manifest.source.libreWinFormsCommit} does not match current checkout ${expectedLibreWinFormsCommit}.`);
  }
}

if (manifest.packageDirectory !== ".") {
  fail(`Expected preview manifest packageDirectory '.', found ${manifest.packageDirectory}.`);
}

if (!Array.isArray(manifest.packages) || manifest.packages.length !== packageIds.length) {
  fail(`Expected ${packageIds.length} preview manifest package entries, found ${manifest.packages?.length}.`);
}

const expectedIds = new Set(packageIds);
for (const [index, packageId] of packageIds.entries()) {
  const entry = manifest.packages[index];
  if (!entry || entry.id !== packageId) {
    fail(`Expected preview package entry ${index} to be ${packageId}, found ${entry?.id}.`);
  }

  if (!expectedIds.delete(entry.id)) {
    fail(`Unexpected or duplicate preview package id ${entry.id}.`);
  }

  const expectedVersion = ["LibreWPF.Transport", "LibreWPF.ProGPU", "LibreWPF.Sdk"].includes(packageId)
    ? devPackageVersion
    : proGpuPackageVersion;
  if (entry.version !== expectedVersion) {
    fail(`Expected preview package ${packageId} version ${expectedVersion}, found ${entry.version}.`);
  }

  const expectedFile = `${packageId}.${expectedVersion}.nupkg`;
  if (entry.file !== expectedFile) {
    fail(`Expected preview package ${packageId} file ${expectedFile}, found ${entry.file}.`);
  }

  const packagePath = path.join(extractDirectory, expectedFile);
  if (!fs.existsSync(packagePath)) {
    fail(`Missing preview package ${packagePath}.`);
  }

  const bytes = fs.readFileSync(packagePath);
  if (entry.sizeBytes !== bytes.length) {
    fail(`Preview package ${expectedFile} size mismatch: manifest ${entry.sizeBytes}, actual ${bytes.length}.`);
  }

  const sha256 = crypto.createHash("sha256").update(bytes).digest("hex");
  if (entry.sha256 !== sha256) {
    fail(`Preview package ${expectedFile} SHA-256 mismatch.`);
  }
}

if (expectedIds.size !== 0) {
  fail(`Missing preview package ids: ${Array.from(expectedIds).join(", ")}.`);
}
NODE

echo "LibreWPF preview release bundle verification succeeded for ${bundle_output}."
