# Reproducible Windows compiler artifacts

Acceptance application: `ProGPU.Wpf.ShowcaseApp`; action: startup and first input.
ProGPU `20bbf7f1` adds `eng/stage-dxc-compiler.ps1`, a pinned redistributable
compiler package description, 17 input tests and Windows CI artifact staging.
This removes manual Windows SDK DLL discovery from explicit DXC deployment.

The exact Microsoft.Direct3D.DXC 1.8.2502.8 archive, author certificate and each
pure x64/ARM64 library are pinned. Package signatures, hashes and actual PE
architecture are verified before atomic publication into a new directory. All
top-level license notices and the original nuspec are retained. No source,
package cache, system DLL, renderer default or existing artifact is overwritten.
WARP is neither an input nor an output of the staging tool.

Microsoft's [package documentation](https://www.nuget.org/packages/Microsoft.Direct3D.DXC/1.8.2502.8)
identifies the redistributable and its license mapping. The actual signed package
and notices were inspected. The newer 1.9.2607.13 package's ARM64-directory
libraries failed the current pure-ARM64 PE admission; folder labels are not
architecture evidence and the guard was not loosened. The selected package is
the previously tested compiler release family, not the latest DXC release.

## Evidence

- All 17 offline input checks pass on macOS and Windows.
- Signed staging passes for x64 and ARM64 on macOS, and ARM64 in the VM.
- The workflow passes actionlint; current-head hosted artifact jobs remain required.
- Windows loads the two actual staged compiler DLLs, version 1.8.2502.8, from
  `C:\ProGPU.DxcPackage-1.8.2502.8-arm64` through the existing explicit directory
  setting. No diagnostic managed assembly or modified shader is used.
- The complete development-WARP consumer exits 0 (process 8800), including ABI,
  document/inline layout, cubic control hull, retained MIL rendering and all
  native owner/participation/generation/region-first queries.
- The matched system-WARP consumer passes rendering, then exits `0xC0000005`
  before first-query readback (process 10772). Both processes are terminal.
- Native library hash remains
  `e696e6a9809f82d5baa5d45c3fcabbf11a19d7888e6c05a7cc663f3893337d52`.
  The exact compiler hashes and signature pin are in ProGPU's committed JSON.
- Linux Build `34786836956` passes at `7aa2c352`, including the corrected strict
  allocation measurement. This does not imply the whole run or new head is green.

## Workspace recovery and remaining gates

During this batch, the entire local `artifacts/native-core-build.KvxVug`
directory disappeared unexpectedly. Git retained committed ProGPU history.
The source-only worktree was recovered at
`/Users/wieslawsoltes/GitHub/ProGPU-native-mil`, branch
`integration/native-mil-compiler-artifacts`, and this turn's uncommitted staging
code was restored and rechecked there. Large build caches and other removed
worktrees were not recreated. Existing experimental VM build outputs still must
not be mistaken for current product binaries. Dependency pins remain unchanged.

Latest fetched ProGPU main remains an ancestor. The PR update is a normal push
to the existing feature branch. Final feature-enabled WebGPU packaging and its
complete dependency notices, Windows x64/ARM64 runtime qualification, and actual
source application gates remain required before ordered merges. The original
broader compatibility scope stays recorded as deferred, not complete.
