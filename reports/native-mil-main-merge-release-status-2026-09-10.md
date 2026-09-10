# Native MIL main integration and release status

Latest follow-up: [registered source-resource validation](native-mil-resource-registry-validation-2026-09-10.md)
records the public MIL registry correction, passing focused cache/native-input
tests, green hosted Windows x64 native and LibreWinForms checks, and the remaining
broad WPF test and package/application blockers.

## Integrated branches

ProGPU #140 merged at `8842f828a47442dd0d3e583f85c57432e3c448e5` on
2026-09-10 at 11:51 UTC. The native MIL integration merges that main history,
retaining the earlier #155 integration. Twelve ProGPU conflicts are resolved.
LibreWPF also merges `progpu-rendering-port` at `d877d7aeff226d121784fb0723e05079f300b5d5`,
including canonical WinForms changes; fifteen conflicts are resolved.
These are history-preserving merges, not destructive rebases of the shared PRs.

While qualification continued, upstream #123 advanced the base to `2080ea63a`.
Its provenance fixes are integrated with two further conflicts resolved in the
SDK workflow and entry script. Canonical WinForms package closure, source-specific
ProGPU versions and exact package commit checks remain; native renderer/input
selection, explicit CLI-only build mode and successful exact-head native-runtime
staging remain as well. No ancestor-only package substitution was retained.
Actionlint, YAML parsing, Bash syntax, canonical cutover and documentation checks
pass for this merge. Full SDK/package execution remains pending.

The selected ProGPU commit is `9e05651abbe4e6a9ed8adc4445eab9c210b09dde`.
LibreWinForms is aligned through `c67b04a8c0d49fd9bff8f40988ea294d22025f82`
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
   changing assertions; `745c05f8` shares the full ten-case matrix with a focused
   diagnostic entry. `cbbb2aed` repairs an out-of-bounds depth-initialization
   array: cache slots follow the transient slot range but the old array covered
   only transient slots. The old bound provably triggers ASan stack-buffer-overflow
   in cache case 1; the restored fix passes ASan/UBSan across all ten cases.
   All 19 local native suites pass again. All ten focused VM D3D12 cases pass;
   GCC/MSVC, Linux ARM64/x64 and browser WebGPU hosted checks also pass. A full
   Windows run exposed a separate cold-compilation timeout, now resolved in the
   VM by a bounded test allowance: all 20 native suites pass at `03acd40c`.
   Hosted Windows x64 then reached a separate masked-image differential failure:
   maximum channel delta 1, mean 0.092973 versus the existing 0.05 limit. That
   `9e05651a` separates straight-alpha direct image blending from the mixed
   retained image pipeline without changing either contract. Local Metal pixels
   are unchanged, both native providers rebuild, all 19 native suites and all
   121 managed native-interop tests pass. Hosted Windows WARP confirmation is
   still required; no tolerance has been relaxed.
2. Local managed renderer and headless suites are green at the current source
   state. Their fresh hosted jobs and the platform-specific cases skipped on
   macOS still require validation.
3. SVG W3C's 21 resolved differences have now been reviewed against every current
   main and pinned Chrome image. `fc5670fc` removes exactly those entries, leaving
   246 threshold differences, 270 passes and nine exceptions. The 0.10 threshold,
   all 525 fixtures and resvg inventory are unchanged. Existing SVG.NET text,
   animation/DOM, vertical-writing and filter limitations remain documented in
   ProGPU's `docs/svg-system-drawing-w3c-threshold-review-2026-09-10.md`; threshold
   classification is not pixel parity. Hosted W3C and resvg quality both pass
   at `fc5670fc`; the updated production head requires fresh CI.
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
lane with both providers and test/sample targets. All 313 build steps completed
and unqualified ARM64 payloads were staged. The runtime reproduction is running
on the Parallels Display Adapter through D3D12. The first
Windows PowerShell wrapper stopped after restore; the direct PowerShell 7 retry
is recorded in `artifacts/release-build-160cb12b-win-arm64-retry.log` in the guest.
No stale build, staged output or merely linked library is claimed as qualified.

The `160cb12b` VM graphics run passed ordinary and identity/gradient cached
Viewport3D, then lost both colored centers for scale-two cache case 3. After the
depth-array diagnosis the same clean guest source was fast-forwarded to
`cbbb2aed`; full production build-only compilation/staging succeeded again.
The focused D3D12 ten-case run is now in progress with explicit runtime search
paths. Its log is `artifacts/viewport-cbbb2aed-win-arm64-runtime.log` in the guest.

That focused run completed successfully in 226 seconds. The full Windows native
run hit the previous 300-second limit during initial Direct2D work, as did hosted
ARM64 WARP. `03acd40c` increases only that integration test's Windows allowance to
900 seconds, preserving its complete matrix and assertions. Full production
build-only staging succeeded again, followed by all 20 native suites passing in
319.78 seconds; the graphics test itself took 314.06 seconds. Guest log:
`artifacts/native-03acd40c-win-arm64-tests.log`. The selected `fc5670fc` adds only
reviewed SVG inventory/documentation; fresh exact-head qualification still applies.

LibreWPF at `21fbc75dd` passes canonical WinForms source integration, Windows
managed payload and docs CI. LibreWinForms at `a629bbfe` passes canonical source
and visible Windows/Linux package checks, with other package/AppKit jobs pending.
Those are historical heads, not substitutes for CI on the updated dependency pins.

Local memory-safety evidence lives under the prepared ProGPU checkout's
`artifacts/release-hour/viewport-asan-*`: the controlled old-bound run aborts with
`stack-buffer-overflow` on `layer_depth_initialized`; the committed bound was
immediately restored, rebuilt, and its final run passes. Git verifies no local
source difference from `cbbb2aed`. This is component evidence, not package parity.
Superseded ProGPU Build runs were cancelled to free capacity for exact-head run
`34487437908`; the current run and retained SVG review artifacts were preserved.

## Masked-image follow-up at 14:52 UTC

ProGPU `9e05651a` preserves the retained MIL mask shader and uses the existing
straight-alpha shader/SrcAlpha blending only for direct image frames. This is
one additional engine-owned cached pipeline, not extra per-frame work or a CPU
fallback. The strict mean/per-pixel limits remain unchanged. See its
`docs/native-mil-masked-image-blending-2026-09-10.md` for source-contract,
before/after Metal pixels and hosted failure evidence. The Windows ARM64 VM
passed the earlier pipeline with mean 0.037850, so it is not a reproduction of
the hosted WARP rounding failure. Clean exact-head VM compilation is now running.

At `b3720ebfb`, LibreWPF canonical WinForms source integration, Windows managed
payload and documentation CI pass; the SDK job is still pending. At ProGPU
`fc5670fc`, hosted SVG W3C/resvg and all three Avalonia Dawn platform contracts
pass. These are useful prior-head results, not qualification of the new pins.
Superseded Build runs `34487437908` and `34489232357` were subsequently cancelled
to free capacity; no cancelled run may supply release-qualified native payloads.

The [SDK contract validation follow-up](native-mil-sdk-contract-validation-2026-09-10.md)
records current-head Windows 20/20, local renderer/headless/native-host passes,
the repaired final SDK source guard and newly exposed broader bridge-test
failures. These remain separate from native package-mode application qualification.
