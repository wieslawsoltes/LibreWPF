# MVP selection/caret point-input connection

## Acceptance action and source-backed blocker

Application: the complete package-mode `ProGPU.Wpf.MvpApp`, also applicable to
Toolkit editors using source WPF. Action: select text, click through its visible
highlight to position the caret, extend the selection with Shift+End, then type
over that range. This is row 2 application closure, not general input expansion.

`CaretElement.HitTestCore(PointHitTestParameters)` and `CaretSubElement` both
return null. Their portable drawing previously had no equivalent descriptor and
could enter the point index as ordinary selection/caret coverage. The selection
adorner still draws geometry for region queries; dropping the owner/subtree or
marking it invisible is not equivalent. This discrepancy was found by source
tracing, not reproduced at runtime while validation is deferred.

The surrounding plain editor path already has source-owned geometry:
`TextBoxView.OnRender` records its original transparent viewport rectangle, and
`PortableTextLine` delegates hit positions, logical caret movement and selection
ranges to the retained native paragraph. No additional fake editor bounds or
local text composer was added.

## Implementation

ProGPU `bc635859` extends the existing point-region contract with authoritative
Empty own coverage, preserving zero-sized rectangle semantics. Its native MIL
sideband, shared C++ builder scope, managed command scope and typed source
traversal retain region drawing, clips, transforms and source owners. Own scope
ends before descendants. The generated wire layout is still 40 bytes; the
formerly reserved discriminator is now validated `IsEmpty`. Stale native payloads
reject that nonzero value, so final packages must be refreshed together.

Source `CaretElement` and its child now implement the typed descriptor. Both
LibreWPF adapters publish it without reflection, OS-dependent routing or renderer
fallback. No WPF drawing, glyph, brush, selection or caret-blink behavior changed.
See the [ProGPU contract and provenance](../external/ProGPU/docs/native-mil-empty-point-input.md),
including the primary cross-engine reference record, cost and explicit limits.

Authored paired fixtures cover native/managed empty own input, preserved region
geometry, child independence, zero extent versus Empty, retained snapshots,
invalid metadata, balanced scopes and sideband updates. The source caret fixture
checks both real source visuals. The existing MVP live-input gate now injects
Ctrl+A, queries the actual presented renderer owner under the highlight, clicks
at source caret geometry, checks position 2, selects `ve` with Shift+End and
replaces it through host text input. It requires presented content changes after
selection and replacement and retains the original view-model binding assertions.
No stripped-down application or passing skip was introduced.

## Build-only evidence

All .NET operations were serialized; only immutable native builds ran alongside
them. No tests, source verifiers, apps, GPU/VM workloads, benchmarks or CI polling
ran. Automatic CI remains enabled.

| Target | Result | Elapsed / work |
| --- | --- | --- |
| Root SDK 11 preview, `ProGPU.Wpf.Tests`, Release | 116 existing warnings, 0 errors | 27.37 s |
| Root source `PresentationFramework.Tests`, Release, `-m:1` | 7 warnings, 0 errors | 77.88 s |
| ProGPU SDK 10.0.201, `ProGPU.Tests`, Release | 0 warnings, 0 errors | 60.51 s |
| Complete MVP current MainWindow source, isolated existing package feed, NativeMilWgpu/osx-arm64 | 0 warnings, 0 errors | 10.09 s |
| Clean ProGPU `bc635859`, macOS ARM64 CMake | exit 0 | 303 compile/link steps |
| Clean ProGPU `bc635859`, macOS x64 CMake | exit 0 | 303 compile/link steps |

Native builds include both wgpu-native and Dawn, native tests and samples in the
existing AppleClang header-mode profile. Module/import, Windows/Linux, final
package consumption and runtime qualification remain mandatory. The import-based
consumer was updated but not compiled in these header-mode builds.

The isolated MVP project links the current repository MainWindow code into the
previous complete application snapshot under `artifacts/native-core-mvp-build.Bb1vvF`.
It consumes the earlier preview.45/preview.55 development feed, not this change's
final packages; its result proves compilation only. No payloads were restaged.
Native build checkout `artifacts/native-core-build.KvxVug/progpu` is clean and
detached at `bc635859`. The unrelated four ProGPU native edits and performance
artifact deletions remain excluded. Latest fetched main `102e39e5` is an ancestor.
The native contract and MIL coverage generators ran; freshness verifiers remain
deferred as requested.

## Remaining finish requirements

This closes the identified no-point policy connection in code. It does not prove
live selection fidelity, complete all custom HitTestCore contracts, enable
native input defaults or close the application batch. Continue required MVP and
Toolkit/AvalonDock actions and any concrete source-backed blockers. Keep paid
Xceed/SciChart coverage, final exact-head packages, platform/DirectX comparisons,
GPU/SIMD/image/lifetime/performance gates and both PRs' required CI unchanged.
