# Native host query completion checkpoint — 2026-09-09

## Acceptance dependency and bounded outcome

Acceptance applications: the existing package Showcase and Toolkit/AvalonDock.
User actions: clicking source visuals and selecting clipped content.
Source-backed blocker: `ProGpuWpfWindowHost` exposes synchronous point/region
callbacks, while `NativeCompositor` previously exposed only Begin/TryPoll native
queries. Host queries still consult the managed composition target; that does not
qualify native MIL input.

ProGPU commit `aac4baae` adds the shared desktop completion operation needed by
those callbacks. It does not enable the host's native index flag or switch its
queries yet. The next integration must retain pending request ownership on errors,
source-owner ordering/capacity/diagnostics and the exact presented scene snapshot.
Remaining required native hit coverage must be closed before default admission.

## Implemented

- Additive C `progpu_native_engine_wait_hit_test` export for both native providers.
- Dawn waits for the readback map future, not merely submitted queue work;
  wgpu-native uses its existing blocking device poll. No managed polling loop,
  additional submission, CPU hit algorithm or renderer fallback is introduced.
- Dawn asynchronous polling now uses callback publication before reusing state,
  closing a source-backed callback/write race with repeated map requests.
- Shared ordered-list/summary/discard consumption and request retirement.
- Typed span-based `NativeCompositor.WaitGpuHitTest` and scene-qualified
  `NativeGpuHitTestOwnerSnapshot.Wait`; failed waits preserve pending ownership,
  while terminal map failures retire as before. Browser blocking use is rejected.
- Authored polling/wait differential, repeated-map, token/domain/generation,
  capacity-retry, discard and topmost-result fixtures. These have not executed.

Implementation/provenance/complexity details are in
[ProGPU's completion contract](../external/ProGPU/docs/native-mil-hit-test-completion.md).
The managed renderer's existing synchronous GPU query algorithm is unchanged;
both native providers share the new completion/consumption implementation.

## Build-only evidence

Standalone .NET SDK: `/Users/wieslawsoltes/.dotnet/dotnet` (10.0.201).

| Build | Result |
| --- | --- |
| `ProGPU.Tests`, Release (also compiles its referenced headless graph) | Success, 0 warnings, 0 errors; 43.91 seconds |
| `ProGPU.Native.PackageConsumer`, Release with `ProGpuNativeUseProjectReference=true` | Success, 0 warnings, 0 errors; 8.62 seconds |
| Clean native macOS ARM64 at `aac4baae` | All 303 compile/link steps completed, exit 0 |
| Clean native macOS x64 at `aac4baae` | All 303 compile/link steps completed, exit 0 |

Native builds used the clean detached checkout
`artifacts/native-core-build.KvxVug/progpu`, `/usr/bin/clang++`, and the existing
`build-osx-arm64`/`build-osx-x64` directories. Both providers and configured
test/sample targets were built; none was run. C++ modules remain OFF in these
local build directories. The separate Dawn provider runtime fixture is authored,
not covered by these configured compile targets. Its final build/run gate remains.

The managed consumer build used project references, not the package feed. Direct
`cmake --build` did not restage any payload or produce packages. Staged macOS
payloads therefore remain at `e814ca9c`; Windows/Linux payloads and the complete
23-package development feed remain at `da36a718`. Do not combine new managed wait
calls with these older native payloads: final package production must refresh them.

Freshly fetched ProGPU `origin/main` remains
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of the implementation branch.
Unrelated local native edits and deleted performance artifacts were excluded from
the commit and clean native build checkout.

## Still required

Connect all native host owner/candidate callbacks, diagnostics and source refresh
to the presented native scene without stale mappings or a managed index fallback.
Close application-required clip/cache/stroke/input gaps, then feature-freeze and
produce exact-head platform packages. All runtime, differential rendering,
Windows Parallels, browser/module/GCC/MSVC, lifetime, performance/SIMD and required
CI gates remain deferred to final qualification. This checkpoint is not application
input parity, merge readiness or delivery completion.
