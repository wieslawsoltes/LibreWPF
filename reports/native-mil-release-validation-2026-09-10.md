# Native MIL release push — 2026-09-10

User deadline: 10:21–11:21 UTC. Expansion frozen; required gates are not waived.

## Verified progress

- ProGPU is rebased on main `73cda9a5` (merged #155). First release failure fixes
  are pushed at `f99a524f`; see its `docs/native-mil-release-validation-2026-09-10.md`.
- All six baseline native RID builds finished. These are unqualified build-only
  payloads at `2a998c86`, not binaries for the newer fixes.
- LibreWPF Windows managed runtime payload CI passed at `bc6c30a89`; the exact
  artifact was downloaded into the isolated validation worktree.
- Source-built host Release compilation succeeded. The harness now resolves
  source WindowsBase using the selected configuration/TFM rather than Debug.
- At LibreWPF `ecef7f4c1`, with the locally rebuilt ProGPU native fixes, the macOS
  ARM64 native MIL host smoke passed: viewport retention; bitmap DPI/clone/storage;
  ImageBrush invalidation; DrawingImage clear/refill; styled/tabbed/RTL collapse,
  selection and Toolkit header glyph export; WriteableBitmap; geometry selection;
  native owner hit queries; retained window device recovery and presentation.
  Final frame: 23 commands, 20 resources, 9 draws and 5 draw calls.
- The pre-host hyperlink formatting fixture now supplies its source underline
  explicitly. It has no Application theme dictionary; this tests native decoration
  rendering, not application theme loading. Package theme qualification remains.
- Final release fixes are pushed to ProGPU `219df990`, still based on main
  `73cda9a5` (rechecked during this push). They include native alpha/text repairs,
  mask-list lifetime ownership, geometry split handling, complete export lists,
  strict compiler fixes and matching ABI/test assertions. No required gate was
  disabled. LibreWPF pinned that commit at that checkpoint.
- The complete local managed run at `bd4b7313` reported 4,540 pass / 5 fail /
  7 skip (4,552 total). Four failures are cached-stroke coverage/alpha differences.
  The fifth was a stale submission-count assertion, corrected in `219df990` and
  passing in a focused rerun. A full current-head run remains authoritative.
- The complete local native run at `bd4b7313` reported 16 pass / 3 fail (19 total).
  Remaining failures: Direct2D WebGPU Viewport3D sibling/depth pixels; Direct2D
  compatibility Widen checkpoint 344; initial MIL ellipse input build, status 5.
  Geometry utility, native internal and both native text suites passed. The final
  explicit diagnostic table type also rebuilt and passed its native test.
- LibreWPF Windows managed runtime CI passed at `f38af0f3b`, but SDK qualification
  remains pending. ProGPU latest-head CI must rerun after the final compiler/test
  corrections; earlier green jobs do not qualify the new commit.
- Final reruns at ProGPU `219df990`: managed **4,541 passed / 4 failed / 7 skipped**
  (4,552 total), native **16 passed / 3 failed** (19 total). The managed failures
  are exactly the four cached-stroke cases above. The source-built macOS host
  smoke passed again using LibreWPF `ecef7f4c1` source outputs and the rebuilt
  native library, retaining the same window across device recovery. Its managed
  interop dependencies remain the isolated `2a998c86` build; this is explicitly
  not an exact-head complete package qualification. Current-head GitHub CI is
  queued/running, with documentation/API checks green at the latest observation.

## Deadline disposition

The one-hour push has not produced a merge-qualified package Showcase. Both PRs remain
drafts, with required rendering failures and platform/package qualification open.
Do not merge on the strength of the passing source-host smoke. The concrete next
work is the four managed cached-stroke comparisons and three native failures
above, then exact-head complete package production and application/CI gates below.
There is no evidence-backed completion ETA until these runtime failures are fixed;
CI queue time and cross-platform execution are additional to implementation time.

## Required before merge

Application naming is now purpose-based throughout source and delivery assets;
see [naming and validation](../docs/native-mil-application-naming.md). The showcase
and SciChart projects, launchers, resource identities and paired native fixtures
were renamed without changing release admission. ProGPU `2739c702` also fixes
three strict-GCC structured-binding copy warnings in existing native tests.

LibreWPF pinned ProGPU `2747b9b3c1113a35fc51d18dfe79a9f9a5c1ca57` for this checkpoint.
At `5da6b179`, the actual retained EllipseGeometry route uses the existing
canonical full-arc encoder for identity-local solid pens, including explicit
empty dash styles. Sampled, transformed and nonempty dashed pens keep their
existing preparation requirements. Source fixtures verify initial, resized and
cleared geometry, not just the isolated arc encoder.

At `2747b9b3`, cached built-in effects retain the original native input frame.
Nine native cache/effect combinations pass, as do the paired nine managed cases;
three native spatial-mask cases explicitly reject without a partial scene.
The GCC border fixture now checks the renderer's exact join decomposition rather
than a compiler-dependent hardcoded primitive count. See ProGPU's
`docs/native-mil-ellipse-input.md` and `docs/native-mil-cached-effect-input.md`.

The complete macOS run at this head reports **4,541 managed passed / 5 failed /
7 skipped** (4,553 total) and **16 native suites passed / 3 failed** (19 total).
The managed failures are the four cached-stroke pixel comparisons and the
subscriber-free nonclient input allocation assertion (7,968 bytes versus zero).
The native MIL suite now progresses past ellipse and cached-effect input to a
curved tiled-pen fixture failure. The other native failures remain Viewport3D
sibling/depth pixels and Direct2D Widen checkpoint 344. These results are not
complete package/runtime qualification; no required checks were disabled.

The source-built macOS native host smoke passed again against this rebuilt native
library, including retention, source images/text/geometry, native owner input and
device recovery in the existing window (23 commands, 20 resources, 9 draws,
5 draw calls). Its WPF harness/source outputs and managed interop dependencies
remain the isolated earlier builds described above, not newly qualified packages.

The next pinned repair is ProGPU `97eef74f7a5fc191088fa3a176e24706e566a2e8`.
It preserves finite zero-extent stroke centerlines during geometry-local mapping,
without changing fill-area admission. The previously failing broken curve leaves
a horizontal run before an unstroked gap; its bounds were incorrectly rejected
before widening. All 64 tiled-pen combinations now pass, along with three explicit
native horizontal/vertical/rank-one mapped-line cases. Managed linear-stroke tests
pass 19/19 and native contract verification passes. The native MIL suite progresses
to a later collapsed-group brush-table count assertion; full CI is still required.
See ProGPU `docs/native-mil-stroke-spine-bounds.md` for provenance and parity scope.

## Additional release hour and main integration

The user extended the delivery effort by one hour at approximately 11:58 UTC and
requested resolution of conflicts after ProGPU #140 merged. Main advanced to
`8842f828`; ProGPU integration commit `e9c1ce68359539a30b5226f915d7446d31913b0a`
resolves all twelve conflicted files while preserving both histories and their
rendering/input contracts. LibreWPF now pins that integration head. GitHub reports
ProGPU #139 mergeable (no merge conflicts), not release-qualified.

The combined C++ build passed all 306 steps; generated contracts and the complete
80-project package manifest pass. Native tests remain 16/19 with the same three
failing suites. Managed compilation and exact-head CI qualification are ongoing.
See ProGPU `docs/native-mil-main-integration-2026-09-10.md` for conflict decisions.
The additional deadline does not waive runtime/package checks or permit a red merge.

1. Fix remaining ProGPU native, managed cached-stroke, Svg.Skia and CAD browser
   CI failures; rerun all required checks at the actual delivery head.
2. Produce/stage exact-head complete native payloads and SDK packages. Do not
   relabel the older six-RID outputs or use a source/CI bypass.
3. Pass package-mode Showcase and existing application acceptance gates, including
   Windows native/ProGPU comparison and Linux/macOS actions. Source-host success
   is not interchangeable with package startup, VM input or platform parity.
4. Merge ProGPU #139 only with green checks and release evidence, update the
   dependent gitlink/package consumption, then merge LibreWPF #115 after its
   own exact-head gate. Neither draft PR is currently declared merge-ready.

## Deferred, not completed

General Direct2D/Win2D/COM compatibility, complete DirectX parity and remaining
platform modality, document/text and IME contracts remain in the delivery plan.
The shared Metal-surface prerequisite is preserved separately in local ProGPU
commit `123e9ee1`; it is not part of this release or proof of Cocoa modality.
See `docs/native-mil-core-delivery.md` and repository agent rules for the existing
explicit limitations. The broader goal is not complete merely because this host
gate passes.
