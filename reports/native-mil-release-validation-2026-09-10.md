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

## Required before merge

1. Fix remaining ProGPU native, managed cached-stroke, Svg.Skia and CAD browser
   CI failures; rerun all required checks at the actual delivery head.
2. Produce/stage exact-head complete native payloads and SDK packages. Do not
   relabel the older six-RID outputs or use a source/CI bypass.
3. Pass package-mode MVP and existing application acceptance gates, including
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
