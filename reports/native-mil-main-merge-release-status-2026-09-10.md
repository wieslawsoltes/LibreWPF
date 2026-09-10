# Native MIL main integration and release status

## Integrated branches

ProGPU #140 merged at `8842f828a47442dd0d3e583f85c57432e3c448e5` on
2026-09-10 at 11:51 UTC. The native MIL integration merges that main history,
retaining the earlier #155 integration. Twelve ProGPU conflicts are resolved.
LibreWPF also merges `progpu-rendering-port` at `d877d7aeff226d121784fb0723e05079f300b5d5`,
including canonical WinForms changes; fifteen conflicts are resolved.
These are history-preserving merges, not destructive rebases of the shared PRs.

The selected ProGPU commit is `160cb12b87bc45f736df23425d5c0a7bdd8c76ca`.
LibreWinForms is aligned through `7d22e30b1983c908127095373bdf9a83e24f5673`
in [dependency PR #29](https://github.com/wieslawsoltes/LibreWinForms/pull/29).
Its base `12b4a1be0` has the same tree as the earlier `5aa13b540` pin; only the
ProGPU gitlink and alignment documentation change. Both consumer pins now match.
The root gitlink is updated directly; the user's pre-existing dirty physical
ProGPU/LibreWinForms checkouts are preserved. Neither stale local checkout is
evidence for the selected dependency or a qualified package source.

## Delivered during the additional release hour

- Combined main/native-MIL command metadata stays within the existing compact
  storage limit. Exact antipodal ellipse arcs avoid an extra rounded-up span in
  both managed and C++ resolvers. System.Drawing passes 621/621 tests locally.
- Shared R8 mask writes preserve earlier coverage. The new retained cached-stroke
  edge regression passes; the four broader stroke image comparisons still fail.
- Native offscreen 3D keeps pixel crop separate from model transforms and retains
  the full camera viewport. The Direct2D WebGPU suite passes locally.
- Direct2D capacity failures retain the expected HRESULT. Native fixture audits
  preserve exact semantic coverage, transaction identities and fail-closed cases.
- SVG artifacts now retain passing frames for required review of resolved
  differences; no tolerance or expected-results entry was changed.

Follow-up after the hour: native MIL now preserves sharp four-line rectangle
spines when either radius is zero and accepts finite zero-height stroke contours
before widening. Both radius axes have paired native/managed coordinate checks;
24 focused managed tests pass. The portable Direct2D suite now passes after
correcting its positive opacity-brush setup. A Windows test-buffer type mismatch
found by MSVC is corrected, pending fresh Windows CI. See the
[stroke preparation report](../external/ProGPU/docs/native-mil-sharp-stroke-spine-2026-09-10.md).

Latest follow-up: grouped/direct source radius normalization and canonical ellipse
strokes agree, and cached coverage tests use the correct once-composited opacity
reference. Full local native tests pass 19/19; managed renderer tests pass 4,576
with seven existing platform skips; headless tests pass 280/280. The focused
cached-picture run passes 39/39. Native contract verification passes. The final
MIL fixture repair registers PathGeometry with its actual type ID, preserving
all gap/dash assertions. See the pinned
[grouped stroke validation report](https://github.com/wieslawsoltes/ProGPU/blob/160cb12b87bc45f736df23425d5c0a7bdd8c76ca/docs/native-mil-group-stroke-validation-2026-09-10.md).

See the pinned ProGPU documentation:
[main integration](../external/ProGPU/docs/native-mil-main-integration-2026-09-10.md),
[render-target fixes](../external/ProGPU/docs/native-mil-render-target-corrections-2026-09-10.md),
and [contract audit](../external/ProGPU/docs/native-mil-post-merge-contract-audit-2026-09-10.md).
The [canonical WinForms merge decisions](../docs/native-mil-winforms-main-integration-2026-09-10.md)
describe the source ownership and package guards retained in LibreWPF.

## Merge blockers

1. ProGPU local native suites now pass 19/19. Fresh compiler/platform CI still
   must pass. Hosted MSVC compiled at `978a62eb` but its cached Viewport3D image
   test lost left-sibling pixels; local Metal and hosted Linux Vulkan pass that
   suite. This Windows-specific GPU failure remains open. The separate Windows
   aliased-path size-query fixture now expects insufficient-buffer correctly.
2. Local managed renderer and headless suites are green at the current source
   state. Their fresh hosted jobs and the platform-specific cases skipped on
   macOS still require validation.
3. SVG W3C reports 21 resolved known differences requiring image review before
   expected-results maintenance. The prior artifacts deleted those passing PNGs;
   run `34479034756` now retains them in artifact `10153029058`. Complete visual
   review remains pending. Do not relax image thresholds.
4. The canonical LibreWinForms dependency pin is now aligned in PR #29 and selected
   here. That dependency PR and the actual canonical integration/package gates
   still require validation and ordered merge. The exact source-graph check is
   preserved; matching gitlinks alone are not runtime qualification.
5. Complete exact-head native payloads/packages and source/package application
   qualification on macOS, Linux and Windows, including the native oracle, remain
   outstanding. Earlier payloads and smoke passes are historical, not substitutes.
6. Green exact-head CI is required. LibreWPF Windows managed-payload and docs jobs
   passed at `61288438f`; its canonical WinForms and SDK jobs failed. The SDK job
   correctly rejected a cancelled exact-ProGPU build. New heads require new CI.

The additional hour began at 11:58 UTC with a 12:58 UTC target. The merge target
is not achieved. The dependent PRs remain drafts; do not force-merge red checks, activate
auto-merge, or describe the broader DirectX/Direct2D/Win2D goal as complete.
Feature freeze remains active: fix these release blockers before broader APIs.
