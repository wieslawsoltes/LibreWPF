#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
librewinforms_root="${repo_root}/external/LibreWinForms"
progpu_root="${repo_root}/external/ProGPU"
configuration="${CONFIGURATION:-Release}"
target_framework="net10.0"

if [[ ! -f "${librewinforms_root}/src/System.Windows.Forms/System.Windows.Forms.csproj" ]]; then
  echo "Initialize the external/LibreWinForms submodule before running the canonical integration gate." >&2
  exit 1
fi

if [[ ! -f "${progpu_root}/src/System.Drawing.Common/System.Drawing.Common.csproj" ]]; then
  echo "Initialize the external/ProGPU submodule before running the canonical integration gate." >&2
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

echo "Building canonical LibreWinForms for ${target_framework} from the aligned ProGPU checkout..."
"${librewinforms_root}/eng/common/dotnet.sh" build \
  "${librewinforms_root}/src/System.Windows.Forms/System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  -p:NetCurrent="${target_framework}" \
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
system_printing_ref_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/System.Printing/ref/System.Printing-ref.csproj"
presentation_framework_ref_project="${repo_root}/src/Microsoft.DotNet.Wpf/src/PresentationFramework/ref/PresentationFramework-ref.csproj"

for prerequisite in \
  "${primitive_project}" \
  "${system_xaml_project}" \
  "${windows_base_project}" \
  "${progpu_interop_project}"
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

echo "Canonical WindowsFormsIntegration source gate succeeded for LibreWinForms $(git -C "${librewinforms_root}" rev-parse --short HEAD) and ProGPU $(git -C "${progpu_root}" rev-parse --short HEAD)."
