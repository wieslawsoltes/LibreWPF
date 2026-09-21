# Native MIL deferred dialog Hide — 2026-09-09

## Application dependency and delivered change

Acceptance path: LibreWPF Showcase About dialog, Hide while native event dispatch is
active. The source-backed blocker was immediate native visibility mutation in
`ProGpuWpfWindowHost.Hide`, despite native destruction already respecting the
ProGPU modal session lease. This was not reproduced in an application: execution
remains deferred until implementation freeze.

ProGPU commit `06b0c1894d9cf363ada55a7866b6a2d1f4de28ed` adds window-scoped release
completion to the existing coordinator. It ends all existing matching sessions
after nested sessions and active polls unwind, releases retained identities,
restores parent state, then invokes callbacks outside native transitions.
Callbacks can reenter; independent callback failures do not skip other ready
callbacks or parent cleanup. Native End failure retains the host and becomes a
terminal explicit coordinator failure, without retrying a potentially consumed
token. Failed native cleanup does not publish a successful completion callback.

The WPF host requests that completion before hiding, coalesces pending hides and
checks latest Show/Hide/disposal state and native leases at completion. A later
Show is not undone by an earlier pending Hide. Both renderers use this host path;
no scene rendering, shader, MIL wire format or CPU fallback algorithm changed.
Lifecycle control flow is dependency-bound, not an independent-lane SIMD kernel.
No new native ABI calls or reflection were introduced.

## Compilation only

Serialized commands from the WPF root, using its pinned dotnet SDK:

```sh
./.dotnet/dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Final backend fixture build: 0 warnings, 0 errors. Final WPF bridge fixture build:
116 warnings, 0 errors. Eight backend lifecycle fixtures and one host source-order
fixture were authored, not executed. The latter guards the actual host source;
it does not exercise a native window. No tests, verification scripts, applications,
VM/GPU workloads, benchmarks or CI polling ran. This is not runtime qualification
or a claim that the warnings or required PR checks are resolved.

Latest fetched ProGPU main `102e39e5088b462624da6296ff70a43ed2c5d8b4` is included.
Unrelated native semantic-state changes and deleted performance artifacts remain
untouched and excluded from these commits.

## Remaining core requirements

Automatic Cocoa ShowDialog admission is still disabled: source modal-gate release
and focus restoration must consume actual native completion, and separately
surfaced popups need genuine modal admission. GLFW-owned NSWindows are not silently
converted into NSPanels or switched to owner-surface replay. Linux native modality,
complete platform payload/package production and Windows SDK admission remain
open. The previously reported Parallels/storage failure was not retried here.

After core implementation closure, run the existing package Showcase/third-party,
cross-platform comparison, lifetime, performance and exact-head CI gates. This
checkpoint closes only the identified native host Hide branch, not an application
milestone or the overall native MIL/DirectX objective.

Contract and primary-source provenance are in the ProGPU
[Cocoa modal-session documentation](../external/ProGPU/docs/native-mil-cocoa-modal-session.md#deferred-native-hide-completion).
