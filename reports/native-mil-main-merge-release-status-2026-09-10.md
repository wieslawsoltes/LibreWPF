# Native MIL main integration and release status

## Integrated branches

ProGPU #140 merged at `8842f828a47442dd0d3e583f85c57432e3c448e5` on
2026-09-10 at 11:51 UTC. The native MIL integration merges that main history,
retaining the earlier #155 integration. Twelve ProGPU conflicts are resolved.
LibreWPF also merges `progpu-rendering-port` at `d877d7aeff226d121784fb0723e05079f300b5d5`,
including canonical WinForms changes; fifteen conflicts are resolved.
These are history-preserving merges, not destructive rebases of the shared PRs.

The selected ProGPU commit is `b88034192307d0f12f76af1461b54441c4041cf3`.
LibreWinForms is aligned through `01243b3bc7999ce879fc793c4ed291fe841d8219`
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

1. ProGPU local native suites pass 19/19 at `160cb12b`; twenty repeated local
   Direct2D WebGPU runs also pass. Hosted MSVC still loses the left cached
   Viewport3D sibling, while GCC/Vulkan loses a nested sibling. This is not
   Windows-only. Linux ARM64 passed all 23 C++ suites but failed a later exact
   front/back lighting check. That fixture reversed winding without normals;
   `c6b8821e` corrects the source normals and the exact local Metal gate now
   passes. `b8803419` adds failure-only coverage/cache diagnostics without
   changing assertions. Fresh CI and the cached-sibling repair remain open.
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

## Windows reproduction at 14:00 UTC

The existing clean `C:\pgpu-rebase-2a998c86` VM checkout was fast-forwarded to
`160cb12b` after verifying its source status and absence of active builds.
PowerShell 7 runs the full `-BuildOnly -Rid win-arm64 -Compiler MSVC` production
lane with both providers and test/sample targets. Both providers have linked;
the remaining test compilation and runtime reproduction are pending. The first
Windows PowerShell wrapper stopped after restore; the direct PowerShell 7 retry
is recorded in `artifacts/release-build-160cb12b-win-arm64-retry.log` in the guest.
No stale build, staged output or merely linked library is claimed as qualified.
