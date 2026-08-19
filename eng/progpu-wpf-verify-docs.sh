#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/eng/progpu-preview-package-list.sh"

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -Fq -- "${text}" "${repo_root}/${file}"; then
    echo "Missing '${text}' in ${file}." >&2
    exit 1
  fi
}

require_text ".github/workflows/progpu-wpf-sdk.yml" "./eng/progpu-wpf-sdk-ci.sh"
require_text ".github/workflows/progpu-wpf-sdk.yml" "PROGPU_WPF_PROGPU_PACKAGE_VERSION: 0.1.0-preview.50"
require_text ".github/workflows/progpu-wpf-sdk.yml" "librewpf-ci-packages-"
require_text ".github/workflows/progpu-wpf-sdk.yml" "if-no-files-found: error"
require_text ".github/workflows/progpu-wpf-sdk.yml" "./eng/progpu-wpf-linux-xwayland-smoke.sh"
require_text ".github/workflows/progpu-wpf-release.yml" "NUGET_API_KEY"
require_text ".github/workflows/progpu-wpf-release.yml" "librewpf-v*"
require_text ".github/workflows/progpu-wpf-release.yml" "refs/tags/librewpf-v"
require_text ".github/workflows/progpu-wpf-release.yml" "librewpf-packages-"
require_text ".github/workflows/progpu-wpf-release.yml" "default: 0.1.0-preview.42"
require_text ".github/workflows/progpu-wpf-release.yml" "default: 0.1.0-preview.50"
require_text ".github/workflows/progpu-wpf-release.yml" 'name: librewpf-packages-${{ needs.promote-qualified-preview.outputs.version || needs.preview.outputs.version }}'
require_text ".github/workflows/progpu-wpf-release.yml" "Create GitHub Release"
require_text ".github/workflows/progpu-wpf-release.yml" "gh release create"
require_text ".github/workflows/progpu-wpf-release.yml" "--generate-notes"
require_text "README.md" "Tag releases promote and re-verify the exact package artifact"
require_text "docs/progpu-wpf-release.md" 'terminal-success `LibreWPF Build` run for the exact tagged commit'
require_text ".github/workflows/progpu-wpf-release.yml" "Stage exact ProGPU release packages"
require_text ".github/workflows/progpu-wpf-release.yml" "LibreWPF.Transport LibreWPF.ProGPU LibreWPF.Sdk"
require_text ".github/workflows/progpu-wpf-docs.yml" "librewpf-docs"
require_text "README.md" "# LibreWPF ProGPU Port"
require_text "roadmap.md" "# LibreWPF Cross-Platform Roadmap"
require_text "Directory.Build.props" "<PackageTags Condition=\"'\$(PackageTags)' == ''\">librewpf;progpu;webgpu;silk.net;xaml;cross-platform;desktop</PackageTags>"
require_text "docs/progpu-wpf-release.md" "LibreWPF.Sdk"
require_text "docs/progpu-wpf-release.md" "gh release create --generate-notes"
require_text "docs/progpu-wpf-release.md" "exact ProGPU release packages"
require_text "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj" "<PackageName>LibreWPF.Transport"
require_text "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.csproj" "<PackageName>LibreWPF.Transport"
require_text "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.csproj" "<PackageDescription>LibreWPF transport package"
require_text "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.csproj" "<PackageTags>librewpf;progpu;xaml;themes;transport</PackageTags>"
require_text "src/ProGPU.Wpf/ProGPU.Wpf.csproj" "<PackageId>LibreWPF.ProGPU</PackageId>"
require_text "src/ProGPU.Wpf/ProGPU.Wpf.csproj" "<Description>LibreWPF cross-platform ProGPU rendering host"
require_text "external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj" "<PackageId>LibreWPF.Interop</PackageId>"
require_text "external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj" "<PackageDescription>LibreWPF portable interop contracts"
require_text "packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj" "<PackageName>LibreWPF.Sdk"
require_text "packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj" "<PackageDescription>LibreWPF MSBuild SDK"
require_text "samples/ProGPU.Wpf.HelloApp/README.md" "# LibreWPF Hello App"
require_text "samples/ProGPU.Wpf.MvpApp/README.md" "# LibreWPF MVP App"
require_text "samples/ProGPU.Wpf.ToolkitApp/README.md" "# LibreWPF Toolkit App"
require_text "samples/ProGPU.Wpf.XceedPaidApp/README.md" "# LibreWPF Paid Xceed Toolkit + DataGrid"
require_text "samples/ProGPU.Wpf.SciChartMvpApp/README.md" "# LibreWPF SciChart MVP App"

if grep -Fq "<PackageTags>librewpf;wpf;" "${repo_root}/packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj"; then
  echo "LibreWPF.Sdk package tags should not use WPF as a public package-brand tag." >&2
  exit 1
fi

if grep -Fq "<PackageTags>librewpf;progpu;webgpu;silk.net;wpf;" "${repo_root}/src/ProGPU.Wpf/ProGPU.Wpf.csproj"; then
  echo "LibreWPF.ProGPU package tags should not use WPF as a public package-brand tag." >&2
  exit 1
fi

for package_id in "${progpu_preview_package_ids[@]}"; do
  require_text "README.md" "| \`${package_id}\` |"
  require_text "docs/progpu-wpf-release.md" "\`${package_id}\`"
done

echo "LibreWPF documentation/package table verification succeeded."
