# Native MIL main integration and release status

## Integrated branches

ProGPU #140 merged at `8842f828a47442dd0d3e583f85c57432e3c448e5` on
2026-09-10 at 11:51 UTC. The native MIL integration merges that main history,
retaining the earlier #155 integration. Twelve ProGPU conflicts are resolved.
LibreWPF also merges `progpu-rendering-port` at `d877d7aeff226d121784fb0723e05079f300b5d5`,
including canonical WinForms changes; fifteen conflicts are resolved.
These are history-preserving merges, not destructive rebases of the shared PRs.

The selected ProGPU commit is `ebd12fc0c2c706d66e6db22356b0b4cca9f35e39`.
LibreWinForms is aligned through `26c942dc892e46dc220f797324ebd8ba9739445e`
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

See the pinned ProGPU documentation:
[main integration](../external/ProGPU/docs/native-mil-main-integration-2026-09-10.md),
[render-target fixes](../external/ProGPU/docs/native-mil-render-target-corrections-2026-09-10.md),
and [contract audit](../external/ProGPU/docs/native-mil-post-merge-contract-audit-2026-09-10.md).
The [canonical WinForms merge decisions](../docs/native-mil-winforms-main-integration-2026-09-10.md)
describe the source ownership and package guards retained in LibreWPF.

## Merge blockers

1. ProGPU local native suites remain 17/19 passing. Later Direct2D composite-mask
   representation and MIL transformed-primitive fixtures still fail; reaching
   later checkpoints is not a whole-suite pass.
2. The managed full run has 4,569 passes, seven platform skips and four cached
   stroke image failures. The newly added edge regression passes separately.
3. SVG W3C reports 21 resolved known differences requiring image review before
   expected-results maintenance. The prior artifacts deleted those passing PNGs;
   a fresh run must publish them. Do not relax image thresholds.
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
is not achieved. Both PRs remain drafts; do not force-merge red checks, activate
auto-merge, or describe the broader DirectX/Direct2D/Win2D goal as complete.
Feature freeze remains active: fix these release blockers before broader APIs.
