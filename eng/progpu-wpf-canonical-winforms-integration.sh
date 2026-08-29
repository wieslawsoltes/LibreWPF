#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
librewinforms_root="${repo_root}/external/LibreWinForms"
progpu_root="${repo_root}/external/ProGPU"
configuration="${CONFIGURATION:-Release}"
target_framework="net10.0"
canonical_support_package_version="${PROGPU_WPF_CANONICAL_SUPPORT_PACKAGE_VERSION:-10.0.10}"
canonical_package_output="${PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/CanonicalWinForms}"

if [[ ! -f "${librewinforms_root}/src/System.Windows.Forms/System.Windows.Forms.csproj" ]]; then
  echo "Initialize the external/LibreWinForms submodule before running the canonical integration gate." >&2
  exit 1
fi

if [[ ! -f "${progpu_root}/src/System.Drawing.Common/System.Drawing.Common.csproj" ]]; then
  echo "Initialize the external/ProGPU submodule before running the canonical integration gate." >&2
  exit 1
fi

expected_librewinforms_commit="$(git -C "${repo_root}" ls-tree HEAD external/LibreWinForms | awk '{ print $3 }')"
librewinforms_commit="$(git -C "${librewinforms_root}" rev-parse HEAD)"
if [[ -z "${expected_librewinforms_commit}" || "${expected_librewinforms_commit}" != "${librewinforms_commit}" ]]; then
  echo "LibreWPF pins LibreWinForms ${expected_librewinforms_commit:-missing}, but the initialized checkout has ${librewinforms_commit}." >&2
  echo "Run 'git submodule update --init external/LibreWinForms' before continuing." >&2
  exit 1
fi

librewinforms_progpu_commit="$(git -C "${librewinforms_root}" ls-tree HEAD external/ProGPU | awk '{ print $3 }')"
progpu_commit="$(git -C "${progpu_root}" rev-parse HEAD)"
if [[ -z "${librewinforms_progpu_commit}" || "${librewinforms_progpu_commit}" != "${progpu_commit}" ]]; then
  echo "LibreWinForms pins ProGPU ${librewinforms_progpu_commit:-missing}, but LibreWPF has ${progpu_commit}." >&2
  exit 1
fi

dotnet_command="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet_command}" ]]; then
  dotnet_command="$(command -v dotnet || true)"
fi
if [[ -z "${dotnet_command}" ]] || ! "${dotnet_command}" msbuild -version >/dev/null 2>&1; then
  echo "A .NET SDK compatible with LibreWPF's global.json is required." >&2
  exit 1
fi

if [[ "${PROGPU_WPF_RUN_DRAWING_QUALITY_GATES:-1}" == "1" ]]; then
  echo "Verifying the pinned ProGPU System.Drawing API contract..."
  (cd "${progpu_root}" && ./eng/progpu-verify-system-drawing-api.sh)

  echo "Running the pinned ProGPU System.Drawing quality and allocation gates..."
  "${dotnet_command}" test \
    "${progpu_root}/src/System.Drawing.Common.Tests/System.Drawing.Common.Tests.csproj" \
    --configuration "${configuration}" \
    --verbosity minimal
fi

echo "Building canonical LibreWinForms runtime and design assemblies for ${target_framework} from the aligned ProGPU checkout..."
"${librewinforms_root}/eng/common/dotnet.sh" build \
  "${librewinforms_root}/src/System.Windows.Forms.Design/src/System.Windows.Forms.Design.csproj" \
  --configuration "${configuration}" \
  --no-incremental \
  -p:NetCurrent="${target_framework}" \
  -p:SystemCodeDomPackageVersion="${canonical_support_package_version}" \
  -p:LibreWinFormsReferenceMode=Project \
  -p:LibreWinFormsUseProGpuSystemDrawing=true \
  -p:LibreWinFormsProGpuSourceRoot="${progpu_root}/" \
  -p:ContinuousIntegrationBuild=true

winforms_assembly_root="${librewinforms_root}/artifacts/bin/System.Windows.Forms/${configuration}/${target_framework}/"
progpu_interop_root="${progpu_root}/src/ProGPU.Wpf.Interop/bin/${configuration}/${target_framework}/"

echo "Building the serialized LibreWPF managed foundation..."
primitive_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/System.Windows.Primitives/System.Windows.Primitives.csproj"
system_xaml_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/System.Xaml/System.Xaml.csproj"
windows_base_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/WindowsBase/WindowsBase.csproj"
progpu_interop_project="${progpu_root}/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj"
presentation_build_tasks_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
system_printing_ref_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/System.Printing/ref/System.Printing-ref.csproj"
presentation_framework_ref_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/PresentationFramework/ref/PresentationFramework-ref.csproj"

for prerequisite in \
  "${primitive_project}" \
  "${system_xaml_project}" \
  "${windows_base_project}" \
  "${progpu_interop_project}" \
  "${presentation_build_tasks_project}"
do
  "${dotnet_command}" restore "${prerequisite}" --disable-parallel --verbosity minimal
done

"${dotnet_command}" msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:RestoreManagedTransport \
  -property:Configuration="${configuration}" \
  -property:ProGpuWpfCanonicalWinFormsIntegration=true \
  -verbosity:minimal

echo "Building the CsWin32 primitive prerequisite before its friend assemblies..."
# CsWin32 0.3.269 emits an empty implementation for this project when Arcade's
# ContinuousIntegrationBuild path mapping is enabled. Keep the isolated generator
# invocation deterministic through its explicit inputs and validate its consumers
# under ContinuousIntegrationBuild below.
"${dotnet_command}" build \
  "${primitive_project}" \
  --configuration "${configuration}" \
  --no-restore \
  --no-dependencies \
  -t:Rebuild \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=false \
  --verbosity minimal

echo "Building System.Xaml and portable WindowsBase in isolated compiler processes..."
"${dotnet_command}" build "${system_xaml_project}" \
  --configuration "${configuration}" \
  --no-restore \
  --no-dependencies \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=true \
  --verbosity minimal
"${dotnet_command}" build "${windows_base_project}" \
  --configuration "${configuration}" \
  --no-restore \
  --no-dependencies \
  -t:Rebuild \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=true \
  --verbosity minimal

echo "Building the aligned ProGPU WPF interop assembly..."
"${dotnet_command}" build "${progpu_interop_project}" \
  --configuration "${configuration}" \
  --no-restore \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=true \
  --verbosity minimal

echo "Building the WPF reference and implementation-cycle foundation..."
# Build the reference roots with dependencies so a clean cache contains the
# System.Printing/PresentationFramework contracts and every API/implementation
# cycle assembly before ReachFramework and WindowsFormsIntegration consume them.
"${dotnet_command}" build "${system_printing_ref_project}" \
  --configuration "${configuration}" \
  --no-restore \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=true \
  --verbosity minimal
"${dotnet_command}" build "${presentation_framework_ref_project}" \
  --configuration "${configuration}" \
  --no-restore \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:ContinuousIntegrationBuild=true \
  --verbosity minimal

"${dotnet_command}" msbuild \
  "${repo_root}/eng/ProGPU.Wpf.ValidationGraphs.proj" \
  -target:BuildManagedTransport \
  -property:Configuration="${configuration}" \
  -property:ProGpuWpfCanonicalWinFormsIntegration=true \
  -property:BuildProjectReferences=false \
  -property:ContinuousIntegrationBuild=true \
  -property:UseSharedCompilation=false \
  -verbosity:minimal

canonical_properties=(
  -p:ProGpuWpfCanonicalWinFormsAssemblyRoot="${winforms_assembly_root}"
  -p:ProGpuWpfCanonicalProGpuAssemblyRoot="${progpu_interop_root}"
  -p:ContinuousIntegrationBuild=true
)
ref_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/WindowsFormsIntegration/ref/WindowsFormsIntegration-ref.csproj"
implementation_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/WindowsFormsIntegration/WindowsFormsIntegration.csproj"

echo "Building canonical WindowsFormsIntegration reference surface..."
"${dotnet_command}" restore "${ref_project}" --disable-parallel "${canonical_properties[@]}"
"${dotnet_command}" build "${ref_project}" \
  --configuration "${configuration}" \
  --no-restore \
  --no-dependencies \
  -m:1 \
  -warnaserror:MSB3243,MSB3277 \
  "${canonical_properties[@]}"

echo "Building canonical WindowsFormsIntegration implementation..."
"${dotnet_command}" restore "${implementation_project}" --disable-parallel "${canonical_properties[@]}"
"${dotnet_command}" build "${implementation_project}" \
  --configuration "${configuration}" \
  --no-restore \
  --no-dependencies \
  -m:1 \
  -warnaserror:MSB3243,MSB3277 \
  "${canonical_properties[@]}"

ref_output="${repo_root}/artifacts/bin/WindowsFormsIntegration-ref/${configuration}/${target_framework}/WindowsFormsIntegration.dll"
implementation_output="${repo_root}/artifacts/bin/WindowsFormsIntegration/${configuration}/${target_framework}/WindowsFormsIntegration.dll"
if [[ ! -f "${ref_output}" || ! -f "${implementation_output}" ]]; then
  echo "Canonical WindowsFormsIntegration did not produce both reference and implementation assemblies." >&2
  exit 1
fi

librewinforms_short_commit="$(git -C "${librewinforms_root}" rev-parse --short=8 HEAD)"
librewpf_short_commit="$(git -C "${repo_root}" rev-parse --short=8 HEAD)"
progpu_short_commit="$(git -C "${progpu_root}" rev-parse --short=8 HEAD)"
canonical_package_version="${PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_VERSION:-0.1.0-canonical.${librewinforms_short_commit}.${librewpf_short_commit}}"
progpu_source_package_version="${PROGPU_WPF_CANONICAL_PROGPU_PACKAGE_VERSION:-0.1.0-source.${progpu_short_commit}}"
canonical_forms_package="${canonical_package_output}/LibreWinForms.System.Windows.Forms.${canonical_package_version}.nupkg"
canonical_backend_package="${canonical_package_output}/LibreWinForms.ProGPU.${canonical_package_version}.nupkg"
canonical_integration_package="${canonical_package_output}/LibreWinForms.WindowsFormsIntegration.${canonical_package_version}.nupkg"

mkdir -p "${canonical_package_output}"
rm -f \
  "${canonical_forms_package}" \
  "${canonical_package_output}/LibreWinForms.System.Windows.Forms.${canonical_package_version}.snupkg" \
  "${canonical_backend_package}" \
  "${canonical_package_output}/LibreWinForms.ProGPU.${canonical_package_version}.snupkg" \
  "${canonical_integration_package}" \
  "${canonical_package_output}/LibreWinForms.WindowsFormsIntegration.${canonical_package_version}.snupkg"

echo "Packing the exact ProGPU drawing dependency closure..."
PROGPU_CONFIGURATION="${configuration}" \
PROGPU_PACKAGE_VERSION="${progpu_source_package_version}" \
PROGPU_PACKAGE_OUTPUT="${canonical_package_output}" \
PROGPU_PACKAGE_GROUP=drawing-runtime \
  "${progpu_root}/eng/progpu-pack.sh"

for package_spec in \
  "src/ProGPU.DirectX/ProGPU.DirectX.csproj|ProGPU.DirectX" \
  "src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj|LibreWPF.Interop"
do
  package_project="${package_spec%%|*}"
  package_id="${package_spec##*|}"
  rm -f \
    "${canonical_package_output}/${package_id}.${progpu_source_package_version}.nupkg" \
    "${canonical_package_output}/${package_id}.${progpu_source_package_version}.snupkg"
  "${dotnet_command}" pack \
    "${progpu_root}/${package_project}" \
    --configuration "${configuration}" \
    --output "${canonical_package_output}" \
    --verbosity minimal \
    -p:Version="${progpu_source_package_version}" \
    -p:PackageVersion="${progpu_source_package_version}" \
    -p:ContinuousIntegrationBuild=true
done

echo "Packing canonical System.Windows.Forms ${canonical_package_version}..."
NetCurrent="${target_framework}" \
  "${librewinforms_root}/eng/common/dotnet.sh" pack \
  "${librewinforms_root}/packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --output "${canonical_package_output}" \
  -p:Version="${canonical_package_version}" \
  -p:PackageVersion="${canonical_package_version}" \
  -p:NetCurrent="${target_framework}" \
  -p:LibreWinFormsSkipCanonicalPackageBuild=true \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_source_package_version}" \
  -p:RestoreAdditionalProjectSources="${canonical_package_output}" \
  -p:ContinuousIntegrationBuild=true

echo "Packing canonical LibreWinForms.ProGPU ${canonical_package_version}..."
NetCurrent="${target_framework}" \
  "${librewinforms_root}/eng/common/dotnet.sh" pack \
  "${librewinforms_root}/packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj" \
  --configuration "${configuration}" \
  --output "${canonical_package_output}" \
  -p:Version="${canonical_package_version}" \
  -p:PackageVersion="${canonical_package_version}" \
  -p:NetCurrent="${target_framework}" \
  -p:LibreWinFormsCanonicalPackageVersion="${canonical_package_version}" \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_source_package_version}" \
  -p:RestoreAdditionalProjectSources="${canonical_package_output}" \
  -p:ContinuousIntegrationBuild=true

echo "Packing canonical WindowsFormsIntegration ${canonical_package_version}..."
"${dotnet_command}" pack \
  "${repo_root}/eng/LibreWinForms.WindowsFormsIntegration.Package/LibreWinForms.WindowsFormsIntegration.Package.csproj" \
  --configuration "${configuration}" \
  --output "${canonical_package_output}" \
  -p:Version="${canonical_package_version}" \
  -p:PackageVersion="${canonical_package_version}" \
  -p:LibreWinFormsCanonicalPackageVersion="${canonical_package_version}" \
  -p:RestoreAdditionalProjectSources="${canonical_package_output}" \
  -p:ContinuousIntegrationBuild=true

for package_file in "${canonical_forms_package}" "${canonical_backend_package}" "${canonical_integration_package}"; do
  if [[ ! -f "${package_file}" ]]; then
    echo "Canonical WinForms package was not produced: ${package_file}" >&2
    exit 1
  fi
done

for expected_entry in \
  "lib/${target_framework}/System.Windows.Forms.Design.dll" \
  "ref/${target_framework}/System.Windows.Forms.Design.dll"
do
  if ! unzip -Z1 "${canonical_forms_package}" | grep -Fxq "${expected_entry}"; then
    echo "Canonical System.Windows.Forms package is missing ${expected_entry}." >&2
    exit 1
  fi
done

forms_nuspec="$(unzip -p "${canonical_forms_package}" '*.nuspec')"
if [[ "${forms_nuspec}" != *"<dependency id=\"System.CodeDom\" version=\"${canonical_support_package_version}\""* ]]; then
  echo "Canonical System.Windows.Forms package does not depend on the qualified System.CodeDom version ${canonical_support_package_version}." >&2
  exit 1
fi

for expected_entry in \
  "lib/${target_framework}/WindowsFormsIntegration.dll" \
  "ref/${target_framework}/WindowsFormsIntegration.dll"
do
  if ! unzip -Z1 "${canonical_integration_package}" | grep -Fxq "${expected_entry}"; then
    echo "Canonical WindowsFormsIntegration package is missing ${expected_entry}." >&2
    exit 1
  fi
done

implementation_hash="$(sha256sum "${implementation_output}" | cut -d' ' -f1)"
packaged_implementation_hash="$(unzip -p "${canonical_integration_package}" "lib/${target_framework}/WindowsFormsIntegration.dll" | sha256sum | cut -d' ' -f1)"
reference_hash="$(sha256sum "${ref_output}" | cut -d' ' -f1)"
packaged_reference_hash="$(unzip -p "${canonical_integration_package}" "ref/${target_framework}/WindowsFormsIntegration.dll" | sha256sum | cut -d' ' -f1)"
if [[ "${implementation_hash}" != "${packaged_implementation_hash}" || "${reference_hash}" != "${packaged_reference_hash}" ]]; then
  echo "Canonical WindowsFormsIntegration package payload does not match the qualified source outputs." >&2
  exit 1
fi

integration_nuspec="$(unzip -p "${canonical_integration_package}" '*.nuspec')"
if [[ "${integration_nuspec}" != *"<dependency id=\"LibreWinForms.System.Windows.Forms\" version=\"${canonical_package_version}\""* ]]; then
  echo "Canonical WindowsFormsIntegration package does not depend on the matching canonical Forms package." >&2
  exit 1
fi

backend_nuspec="$(unzip -p "${canonical_backend_package}" '*.nuspec')"
if [[ "${backend_nuspec}" != *"<dependency id=\"LibreWinForms.System.Windows.Forms\" version=\"${canonical_package_version}\""* \
  || "${backend_nuspec}" != *"<dependency id=\"ProGPU.System.Drawing.Common\" version=\"${progpu_source_package_version}\""* ]]; then
  echo "Canonical LibreWinForms.ProGPU package does not carry the exact Forms and drawing dependencies." >&2
  exit 1
fi

echo "Canonical WindowsFormsIntegration source and package gates succeeded for LibreWinForms ${librewinforms_short_commit} and ProGPU ${progpu_short_commit}."
