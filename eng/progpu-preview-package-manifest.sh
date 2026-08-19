#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.42}"
progpu_package_version="${PROGPU_WPF_PROGPU_PACKAGE_VERSION:-0.1.0-preview.52}"
manifest_path="${PROGPU_WPF_PREVIEW_PACKAGE_MANIFEST:-${package_output}/librewpf-preview-packages-${dev_package_version}.json}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_ids=("${progpu_preview_package_ids[@]}")

package_path() {
  local package_id="$1"
  echo "${package_output}/$(progpu_preview_package_file_name "${package_id}")"
}

file_size() {
  local file="$1"
  if stat -f%z "${file}" >/dev/null 2>&1; then
    stat -f%z "${file}"
  else
    stat -c%s "${file}"
  fi
}

file_sha256() {
  local file="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    sha256sum "${file}" | awk '{print $1}'
  fi
}

git_commit() {
  local git_root="$1"
  git -C "${git_root}" rev-parse --verify HEAD 2>/dev/null || printf 'unknown'
}

git_is_dirty() {
  local git_root="$1"
  if [[ -z "$(git -C "${git_root}" status --porcelain --untracked-files=normal --ignore-submodules=dirty)" ]]; then
    printf 'false'
  else
    printf 'true'
  fi
}

json_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '%s' "${value}"
}

mkdir -p "$(dirname "${manifest_path}")"

for package_id in "${package_ids[@]}"; do
  package_file="$(package_path "${package_id}")"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
done

wpf_commit="$(git_commit "${repo_root}")"
progpu_commit="$(git_commit "${repo_root}/external/ProGPU")"
librewinforms_commit="$(git_commit "${repo_root}/external/LibreWinForms")"
wpf_is_dirty="$(git_is_dirty "${repo_root}")"
progpu_is_dirty="$(git_is_dirty "${repo_root}/external/ProGPU")"
librewinforms_is_dirty="$(git_is_dirty "${repo_root}/external/LibreWinForms")"

{
  printf '{\n'
  printf '  "schemaVersion": 4,\n'
  printf '  "version": "%s",\n' "$(json_escape "${dev_package_version}")"
  printf '  "progpuVersion": "%s",\n' "$(json_escape "${progpu_package_version}")"
  printf '  "source": {\n'
  printf '    "wpfCommit": "%s",\n' "$(json_escape "${wpf_commit}")"
  printf '    "progpuCommit": "%s",\n' "$(json_escape "${progpu_commit}")"
  printf '    "libreWinFormsCommit": "%s",\n' "$(json_escape "${librewinforms_commit}")"
  printf '    "wpfIsDirty": %s,\n' "${wpf_is_dirty}"
  printf '    "progpuIsDirty": %s,\n' "${progpu_is_dirty}"
  printf '    "libreWinFormsIsDirty": %s\n' "${librewinforms_is_dirty}"
  printf '  },\n'
  printf '  "packageDirectory": ".",\n'
  printf '  "packages": [\n'

  first=1
  for package_id in "${package_ids[@]}"; do
    package_file="$(package_path "${package_id}")"
    package_name="$(basename "${package_file}")"
    package_size="$(file_size "${package_file}")"
    package_sha256="$(file_sha256 "${package_file}")"

    if [[ "${first}" == "1" ]]; then
      first=0
    else
      printf ',\n'
    fi

    printf '    {\n'
    printf '      "id": "%s",\n' "$(json_escape "${package_id}")"
    printf '      "version": "%s",\n' "$(json_escape "$(progpu_preview_package_version "${package_id}")")"
    printf '      "file": "%s",\n' "$(json_escape "${package_name}")"
    printf '      "sizeBytes": %s,\n' "${package_size}"
    printf '      "sha256": "%s"\n' "${package_sha256}"
    printf '    }'
  done

  printf '\n'
  printf '  ]\n'
  printf '}\n'
} >"${manifest_path}"

echo "LibreWPF preview package manifest written to ${manifest_path}."
