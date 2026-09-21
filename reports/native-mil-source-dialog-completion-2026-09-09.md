# Native completion before source dialog cleanup — 2026-09-09

## Core acceptance dependency

Application/action: LibreWPF Showcase About dialog, Hide/Close from native event
dispatch, including nested dialogs. The source-backed blocker was unconditional
managed scope disposal in ShowPortableDialog: native End may occur only after
that managed callback returns. No application reproduction was run in this batch.

ProGPU `df3709191e24d56cda29ccde6b8f6496a3097d2b` adds a neutral ReleaseDialog
activation callback and ReleaseAfterNative ownership to the existing input-scope
implementation. Native completion comes first; ready source scopes then unwind
inside-out, publish input gates and dispose their source cleanup snapshots.
Out-of-order native completion cannot bypass an active child source scope.
Ordinary Dispose remains strict and cannot bypass a pending native completion.
Native request failures remain explicit; duplicate completion and repeated release
cannot restore twice. Independent source-cleanup failures do not strand ready
parent scopes. Failed gate publication still clears cleanup ownership while its
synchronization diagnostic prevents unsafe source focus restoration.

LibreWPF captures both RunDialog and ReleaseDialog before Show and rejects missing
capabilities before host creation. Hosts with no native session explicitly complete
the release synchronously. Accepted Hide/Close and finally transfer cleanup once,
capturing activation before source Close clears it. The generation-bound wrapper
retains the actual source active-window/PresentationSource/focus snapshot until
native completion, then clears source ownership before restoring focus. Repeated
ShowDialog on the same Window is rejected while prior release remains pending.
Canceled close retains its current modal scope. Failed Show before activation
uses direct cleanup because no native modal session was admitted.

The existing ProGPU native-session coordinator provides actual native completion;
the host supports the release callback after activation disposal while its native
window is still leased. Both renderer modes share this source/host path. No new
WPF-shaped public callbacks, reflection, native ABI, shaders, scene implementations
or CPU fallback loops were added. Ordered lifecycle work has dependent control
flow; it is not an independent-lane SIMD workload. No speed claim is made.

## Compilation and authored coverage

Builds use the workspace dotnet SDK, serialized with --no-restore -m:1. Commands:

```sh
./.dotnet/dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/ProGPU.Wpf.RealPresentationFrameworkHarness/ProGPU.Wpf.RealPresentationFrameworkHarness.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Backend fixtures: 0 warnings/0 errors. WPF bridge fixtures: 116/0. Source
PresentationFramework fixtures: 6/0 on both the first and final source build. Source-built
PresentationFramework harness: 0/0. Compiler success is not application, native
interaction or CI evidence, and warning resolution is not claimed.

Nine new ProGPU fixtures cover native-session-to-source sequencing, deferred and
out-of-order completion, legacy child disposal, source cleanup/gate failure,
wrong-thread completion, native request failure and the neutral callback contract.
WPF coverage adds host release before/after activation disposal, actual source
Hide/Close with delayed focus restoration, rejection before source creation when
release capability is absent, and the existing cancellation/reopen scenarios.
Source graph assertions prohibit restoring unconditional using disposal.

No tests, verifiers, applications, VM/GPU workloads, benchmarks or CI polling ran.
Unrelated native semantic-state work and deleted performance artifacts are excluded
from these commits. Latest fetched ProGPU main is included. The Parallels/storage
failure from the package-build batch was not retried while source work remained.

## What this does not close

The source gate/focus ordering connection is implemented but unqualified. Genuine
Cocoa native popup admission still prevents automatic AppKit session activation.
Linux native modality, missing platform payloads/full package production and
Windows SDK admission remain open. All existing package-mode application,
third-party/license-controlled, native/managed/Windows comparison, lifetime,
performance and exact-head CI gates remain required after implementation freeze.
The broader DirectX/Direct2D/COM/Win2D scope remains recorded, not completed here.

Contract and original-code provenance:
[ProGPU dialog lifecycle](../external/ProGPU/docs/native-mil-dialog-lifetime.md#native-completion-before-source-input-and-focus-restoration).
