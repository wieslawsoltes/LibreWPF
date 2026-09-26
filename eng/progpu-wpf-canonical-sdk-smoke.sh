#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
canonical_package_source="${PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_SOURCE:-${repo_root}/artifacts/packages/CanonicalWinForms}"
sdk_package_source="${PROGPU_WPF_CANONICAL_SDK_PACKAGE_SOURCE:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package_version="${PROGPU_WPF_CANONICAL_SDK_PACKAGE_VERSION:-0.1.0-preview.65}"
wpf_package_version="${PROGPU_WPF_CANONICAL_WPF_PACKAGE_VERSION:-${sdk_package_version}}"
smoke_source="${repo_root}/eng/LibreWinForms.CanonicalSdkSmoke"
smoke_root="$(mktemp -d -t librewpf-canonical-sdk.XXXXXXXX)"
smoke_config="${smoke_root}/NuGet.config"
smoke_packages="${smoke_root}/packages"

cleanup() {
  rm -rf "${smoke_root}"
}
trap cleanup EXIT

dotnet_command="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet_command}" ]]; then
  dotnet_command="$(command -v dotnet || true)"
fi
if [[ -z "${dotnet_command}" ]]; then
  echo "A dotnet host is required for the canonical SDK consumer smoke." >&2
  exit 1
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

python3 "${repo_root}/eng/tests/test_wpf_sdk_desktop_properties.py" --dotnet "${dotnet_command}"

# The enclosing SDK lane can select canonical packages explicitly. This consumer
# must exercise the packaged SDK's normal UseWindowsForms defaults instead.
unset ProGpuWpfUseCanonicalLibreWinForms ProGpuWpfLibreWinFormsRuntimePackageId
unset ProGpuWpfUseLibreWinForms ProGpuWpfUsePortableWinFormsCompat

resolve_single_version() {
  local package_source="$1"
  local package_id="$2"
  local requested_version="$3"
  local package_file
  local package_name
  local -a candidates=()

  if [[ -n "${requested_version}" ]]; then
    package_file="${package_source}/${package_id}.${requested_version}.nupkg"
    if [[ ! -f "${package_file}" ]]; then
      echo "Missing ${package_id} ${requested_version} in ${package_source}." >&2
      exit 1
    fi
    printf '%s\n' "${requested_version}"
    return
  fi

  shopt -s nullglob
  # Match the version immediately after the complete ID, not longer IDs such
  # as ProGPU.Backend.Dawn when resolving ProGPU.Backend.
  candidates=("${package_source}/${package_id}."[0-9]*.nupkg)
  shopt -u nullglob
  if [[ "${#candidates[@]}" != "1" ]]; then
    echo "Expected one ${package_id} package in ${package_source}, found ${#candidates[@]}." >&2
    exit 1
  fi
  package_name="$(basename "${candidates[0]}")"
  package_name="${package_name#${package_id}.}"
  printf '%s\n' "${package_name%.nupkg}"
}

canonical_package_version="$(resolve_single_version \
  "${canonical_package_source}" \
  "LibreWinForms.System.Windows.Forms" \
  "${PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_VERSION:-}")"
progpu_package_version="$(resolve_single_version \
  "${canonical_package_source}" \
  "ProGPU.Backend" \
  "${PROGPU_WPF_CANONICAL_PROGPU_PACKAGE_VERSION:-}")"

for package_file in \
  "${canonical_package_source}/LibreWinForms.ProGPU.${canonical_package_version}.nupkg" \
  "${canonical_package_source}/LibreWinForms.WindowsFormsIntegration.${canonical_package_version}.nupkg" \
  "${canonical_package_source}/ProGPU.DirectX.${progpu_package_version}.nupkg" \
  "${canonical_package_source}/LibreWPF.Interop.${progpu_package_version}.nupkg" \
  "${sdk_package_source}/LibreWPF.Sdk.${sdk_package_version}.nupkg"
do
  if [[ ! -f "${package_file}" ]]; then
    echo "Canonical SDK consumer package is missing: ${package_file}" >&2
    exit 1
  fi
done

cp "${repo_root}/NuGet.config" "${smoke_config}"
"${dotnet_command}" nuget add source "${canonical_package_source}" \
  --name LibreWinFormsCanonical \
  --configfile "${smoke_config}"
"${dotnet_command}" nuget add source "${sdk_package_source}" \
  --name LibreWpfQualifiedSdk \
  --configfile "${smoke_config}"

package_properties=(
  -p:ProGpuWpfPackageVersion="${wpf_package_version}"
  -p:ProGpuWpfLibreWinFormsPackageVersion="${canonical_package_version}"
  -p:ProGpuWpfLibreWinFormsBackendPackageVersion="${canonical_package_version}"
  -p:ProGpuPackageVersion="${progpu_package_version}"
)

for project_name in LibreWinForms.CanonicalSdkSmoke LibreWinForms.FormsSdkSmoke
do
  for central_packages in false true
  do
    project_root="${smoke_root}/${project_name}-central-${central_packages}"
    smoke_project="${project_root}/${project_name}.csproj"
    mkdir "${project_root}"
    sed "s|Sdk=\"LibreWPF.Sdk/[^\"]*\"|Sdk=\"LibreWPF.Sdk/${sdk_package_version}\"|" \
      "${smoke_source}/${project_name}.csproj" > "${smoke_project}"
    cp "${smoke_source}/Program.cs" "${project_root}/Program.cs"
    cp "${smoke_source}/DesktopPropertyContract.targets" "${project_root}/DesktopPropertyContract.targets"
    consumer_properties=(
      "${package_properties[@]}"
      -p:ManagePackageVersionsCentrally="${central_packages}"
    )

    NUGET_PACKAGES="${smoke_packages}" "${dotnet_command}" restore \
      "${smoke_project}" \
      --configfile "${smoke_config}" \
      --force \
      --no-cache \
      "${consumer_properties[@]}"

    # An equal version from a public feed must not substitute for the newly
    # staged SDK whose default package selection this consumer is qualifying.
    sdk_cache_version="$(printf '%s' "${sdk_package_version}" | tr '[:upper:]' '[:lower:]')"
    if ! cmp -s \
      "${sdk_package_source}/LibreWPF.Sdk.${sdk_package_version}.nupkg" \
      "${smoke_packages}/librewpf.sdk/${sdk_cache_version}/librewpf.sdk.${sdk_cache_version}.nupkg"; then
      echo "${project_name} did not restore the exact staged LibreWPF.Sdk package." >&2
      exit 1
    fi

    NUGET_PACKAGES="${smoke_packages}" "${dotnet_command}" build \
      "${smoke_project}" \
      --configuration Release \
      --no-restore \
      "${consumer_properties[@]}"

    assets_file="${project_root}/obj/project.assets.json"
    for package_identity in \
      "LibreWinForms.System.Windows.Forms/${canonical_package_version}" \
      "LibreWinForms.ProGPU/${canonical_package_version}" \
      "LibreWinForms.WindowsFormsIntegration/${canonical_package_version}"
    do
      if ! grep -Fq "\"${package_identity}\"" "${assets_file}"; then
        echo "${project_name} (central=${central_packages}) restore did not resolve ${package_identity}." >&2
        exit 1
      fi
    done
    if grep -Fq 'LibreWinForms.Compatibility.System.Windows.Forms/' "${assets_file}"; then
      echo "${project_name} (central=${central_packages}) restore retained the transitional Forms package." >&2
      exit 1
    fi

    NUGET_PACKAGES="${smoke_packages}" "${dotnet_command}" run \
      --project "${smoke_project}" \
      --configuration Release \
      --no-build \
      --no-restore \
      "${consumer_properties[@]}"
  done
done

transitive_properties=()
for package_property in "${package_properties[@]}"
do
  transitive_properties+=(--property "${package_property#-p:}")
done
python3 "${repo_root}/eng/tests/test_wpf_sdk_transitive_forms.py" \
  --dotnet "${dotnet_command}" \
  --sdk-root "${smoke_packages}/librewpf.sdk/${sdk_cache_version}" \
  --packages-root "${smoke_packages}" \
  --nuget-config "${smoke_config}" \
  --output-dir "${smoke_root}/transitive-forms" \
  --package-smoke \
  "${transitive_properties[@]}"

echo "Canonical LibreWPF SDK package consumers succeeded with UseWPF true/false and central package management true/false."
