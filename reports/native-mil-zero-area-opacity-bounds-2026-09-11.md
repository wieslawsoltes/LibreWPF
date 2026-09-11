# Exact zero-area opacity bounds

The external application's native render failure was reproduced with a diagnostic
bridge assembly. The rejected source is a Rectangle with descendant bounds
`(0, 0, 0, 4)` and opacity-only isolation. The source advertised those finite,
zero-width bounds as unavailable; the compiler independently required positive
extent even for its existing non-isolated opacity path.

Visual's typed descriptor now admits finite nonnegative extents while preserving
the original rectangle and IsEmpty distinction. The compiler admits zero area
only for the opacity-only path, retaining native visual alpha, children and source
input scopes without allocating an isolation texture. Effects, masks, caches and
visual-brush isolation retain their stricter requirements. Missing, negative and
nonfinite bounds remain failures; there is no layout-rectangle substitute.

The existing native/managed input-scope regression now covers both zero axes,
growth/clear transitions and malformed metadata. Source compilation is running;
regression execution and external application revalidation are pending.

Diagnostic reproduction replaced only the generated external application's bridge
DLL, preserving its original at
`artifacts/native-exact-b54db165.dBPf7B/external-ProGPU.Wpf-before-bounds-diagnostic.dll`.
That modified output is diagnostic, not qualified package evidence; a clean
package rebuild remains required.

Validation: source/harness build passed (two warnings, zero errors, 1m50s).
The focused four opacity/input-scope cases pass across WgpuNative and Dawn with
no skips (68ms). Tests used explicit build plus VSTest, as the repository's
default dotnet-test runner rejects this VSTest project. Test build emitted 116
warnings and no errors; no warnings were disabled.

The diagnostic external app with rebuilt PresentationCore and bridge gets past
the exact zero-width bounds rejection. It next fails native semantic scene
compilation for retained target 4509. That later failure remains undiagnosed and
must be resolved before claiming application closure. The original generated
PresentationCore is preserved at
`artifacts/native-exact-b54db165.dBPf7B/external-PresentationCore-before-zero-bounds.dll`.

The later native failure is now identified as `UnsupportedCommand (5)` by a
failure-message-only ProGPU.Backend.Native diagnostic change (build: zero
warnings/errors, 4.74s). It is not a device-loss or allocation result. The exact
rejected command/input-index contract remains to be localized. No native hit-test
flag, rendering command or unsupported guard was disabled. The diagnostic bridge
change remains in the prepared ProGPU worktree pending the related investigation.

Failure-only C++ probes now localize the rejection to recorded native hit-index
capture, not visual translation. Geometry command 2719, owner 1559 is a cubic
Bézier stroke (kind 4, flags 0, thickness 1). The current geometry capture branch
admits lines and canonical full ellipses, rejecting this rendered cubic. Required
next work is the paired managed/native curve-stroke input contract, not disabling
native hit testing. Temporary probes remain uncommitted in the prepared native
MIL and hit-capture sources; remove them before the final coverage-ledger update
and native-contract verification. Both providers compiled with the probes.

## Stroke-capture checkpoint

ProGPU `312e1a2feff44e41f6fc9db2c0b600a721d37b43` now retains quadratic/cubic
PathStroke controls, renderer-generated connected joins, and open undashed
polyline endpoint caps/interior joins. Application probes identified these in
sequence: cubic command 2719; PATH_JOIN; then an open 18-point, width-1 polyline
with flags 1025. Temporary native probes were removed before committing.

Both providers compile on macOS ARM64. Native MIL regressions pass (0.74s), paired
managed curve encoding tests pass (2 cases, zero skips), and generated MIL
protocol/coverage checks pass. Managed test compilation reports 65 warnings and
zero errors. These are focused checkpoint results, not final package evidence.

The external application still returns UnsupportedCommand with locally replaced
binaries. Localizing the next rejection remains the immediate blocker; neither
native input nor renderer admission has been relaxed. The next exact-head package
build, full application validation, platform gates and all required CI must pass
before ordered merges. ProGPU and LibreWPF had queued checks, not failed checks,
at the pre-push inspection; LibreWinForms was fully green. New pin commits require
their own CI results. Root user-modified physical submodules remain untouched.

## Uncached visual opacity-mask checkpoint

ProGPU implementation `ce8fc414` (documented head `992218ff`) connects uncached
visual opacity masks to the shared typed source-mask input policy. The rejected
layer contained owned descendants, so it was not safe to skip it as unowned
rendering. Source geometry and clips now survive that boundary while its actual
raster mask remains intact. Effect/cache mask combinations remain separate.

Native scene 9842 covers fully transparent gradient masks, own/child owners,
actual clipping, an unaffected sibling and mask removal. The native MIL tests
pass, both providers compile on macOS ARM64, and the complete native contract
verifier passes. The regenerated coverage ledger changes only its decoder digest.

The diagnostic external application now passes native scene/index compilation
and enters rendering. Its next failure is static multi-guideline deformation in
DRAW_STROKE_BATCH (command 222, resource 132). The shared renderer only admits
per-point guidelines for paths today. The next fix must preserve stroke width,
caps/joins, brush mapping, DPI/localization and unsnapped source input, not remove
the guard. Temporary diagnostic probes were removed. These local diagnostic
runs remain distinct from clean exact-package and final CI qualification.
