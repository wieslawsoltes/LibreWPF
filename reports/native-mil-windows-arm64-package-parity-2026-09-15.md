# Windows ARM64 native MIL package parity — 2026-09-15

> Correction: the originally reported same-source text equality did not prove
> native-MIL text execution. A later live-process audit found no explicit
> native runtime selection and no loaded `progpu_native.dll` in the text app.
> The exact-SDK, live-host gated rerun and its non-identical native line-top
> measurements are documented in
> [Windows text runtime admission](native-mil-text-runtime-admission-2026-09-15.md).
> Keep the original numbers below as historical, unqualified output.

## Acceptance and exact provenance

The acceptance application is `ProGPU.Wpf.ShowcaseApp`. The action is a native
Windows ARM64, package-only Release build, pre-display object self-test, and
displayed `Application.Run` self-test with native MIL and native hit testing.
The same source-only `ProGPU.Wpf.TextLayoutParityApp` is then compared with
stock Windows WPF on the same ARM64 guest.

The source is LibreWPF merge `fc42342c1e575786114c14005c094d17751672e8`,
pinning LibreWinForms merge `c5f459c7b078eee47b2c32065ecac0899d87de0a`
and ProGPU main merge `21c60978539ca7893f04491249f08ad11141e475`.
LibreWPF merge-commit Build `34926118582` published the exact
`librewpf-ci-packages-fc42342c1e575786114c14005c094d17751672e8`
artifact (`10380431333`). The packages were staged in a dedicated shared
folder and restored into a private guest NuGet tree. No source assembly or
native DLL was overlaid into the application output.

The Parallels Windows 11 VM is ARM64 (`10.0.26200.9445`) with Parallels Tools
27.0.1. It was resumed from its suspended state for this gate. All graphical
processes used `prlctl exec --current-user`, so WebGPU surfaces were created
in the signed-in desktop session. Build output lived only under
`C:\Temp\LibreWpfMergedArm64Sep15`, not the shared checkout.

## Functional and package evidence

`PresentationBuildTasks`, the unchanged SDK Showcase, the portable text parity
app, and the stock-WPF text oracle built for `win-arm64` with zero warnings and
zero errors. The Showcase apphost PE machine is `0xAA64` (ARM64). Its critical
output hashes match the exact NuGet members:

| Output | SHA-256 |
| --- | --- |
| `PresentationCore.dll` | `8e31da26dd4eb07f9d9edb95260571422a4f5a07d8976aaaefe669472782fab3` |
| `PresentationFramework.dll` | `8452f156ce1633201f992f6498e8cf1adfed41871a490cdbbe9673d48cf5f042` |
| `ProGPU.Wpf.dll` | `08ebe898cd4a2c56bd0ce2505ef1b89a8d40c148bfaf61ed92b10f436d1da602` |
| `progpu_native.dll` | `4695c92a0c05411ce3ac0c83aa0985e61991956fca4aa3d603b6ed83af071d9f` |

The pre-display process exited zero with
`ProGPU WPF Showcase validation succeeded.` The displayed process exited zero
with `ProGPU WPF Showcase Application.Run validation succeeded.` Its trace
passed startup, resources, system commands, storyboards, controls, secondary
window, editor, document, and shutdown.

Both same-source text processes exited zero and reported identical live
metrics:

```text
TEXT_LAYOUT width=211.333 height=93.100 font=14.000 lines=5 tops=0.000,18.620,37.240,55.860,74.480 starts=0,32,58,88,121
```

This is the named text-layout fixture, not a full typography or screenshot
comparison. Native Windows visual ink, mixed scripts, editor/table layout,
modal/popups, and larger DirectX/Direct2D parity still require separate gates.

## Repeatable gate

The package-only Windows Showcase script now accepts
`-TargetArchitecture arm64`, retaining x64 as its default for existing CI.
It checks host architecture, ARM64 PE machine, exact ARM64 package assets,
Showcase markers, and native-WPF text geometry. The CI workflow now has a
separate `windows-11-arm` job consuming the same exact SDK artifact.

The VM's Windows PowerShell 5 policy was Restricted and PowerShell 7 was not
available on PATH. That policy was not overridden. The updated script parsed
with zero errors; the guest validation above used direct `dotnet` and process
commands matching the script's architecture, package, renderer, and hit-test
parameters. The new CI job must execute the actual script under its `pwsh`
runner before this branch is merge-qualified.

The first PR-head SDK producer failed its focused source-graph assertion because
the workflow had gained the intended ARM64 job: exact-head references changed
from 8/18 to 9/20. The guard now checks those counts and the explicit
`windows-11-arm`/`-TargetArchitecture arm64` route. The focused graph test
passes 1/1 locally after that correction; full CI must rerun at the corrected
head rather than treating the first failed run as passing evidence.
