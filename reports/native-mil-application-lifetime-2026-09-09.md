# Native MIL application lifetime — 2026-09-09

## Bounded core dependency

Acceptance applications: SDK/Toolkit/AvalonDock multi-window lifetime and MVP's
existing OnMainWindowClose behavior. Required action: close the first polling
window while another remains, then close/reopen under explicit application
lifetime without replacing the application. The new source lifecycle fixtures
supplement those applications; they are not product-rendering evidence.

Source-backed blocker: Application.Run unconditionally called CriticalShutdown
when the first portable host callback returned. That bypassed the existing source
Window close policy for both OnLastWindowClose and OnExplicitShutdown. Reentering
the existing host Run method also reapplied Show/visibility instead of borrowing
the window's current state. These findings came from source tracing, not runtime
reproduction.

## Implementation

- ProGPU `a902578a` owns the typed PortableApplicationRunLoop coordinator and its synchronous
  lifetime contract. Live-host/spurious-wait returns fail explicitly; exceptions
  retain their identity and do not request shutdown or another rendering backend.
- Source Application supplies actual shutdown state and same-dispatcher live
  activation selection. With no host it blocks on source dispatcher work, removing
  its OperationCompleted hook in finally. Existing Window close logic owns each
  shutdown mode; application code never assigns a replacement MainWindow or
  simulated IsActive. Nested Run is rejected before window-list mutation.
- WpfPortableWindowActivation distinguishes pending first Show from host handoff.
  Hide cancels that pending Show. RunExisting preserves current visibility and
  does not show/activate the host or use the dialog pump/native modal hints.
- The existing application harness now shares the typed interop assembly across
  its isolated load context, uses a typed registrar instead of an outdated
  positional Register invocation, and explicitly requests shutdown after its
  existing application assertions.
- Added three source lifecycle scenarios to the normal SDK gate: last-window,
  main-window, and explicit windowless lifetime with dispatcher-posted reopen.
  They assert source window/host identity, close/dispose/Exit counts, nonzero exit
  preservation and reentrant-Run rejection. Build-packages-only still exits before
  all harness execution; no existing acceptance gate was removed or weakened.
- Authored ProGPU state-machine fixtures and source graph/gate guards. Both
  renderer modes share the host implementation; C++ rendering/scene contracts
  have no source shutdown policy to duplicate and are unchanged in this batch.

The new diagnostic fixture uses public source Application/Window and the public
IPortablePresentationSourceHost contract through the isolated assembly loader.
Its reflection is diagnostic-only with a documented direct-reference exit path;
product code remains typed and reflection-free. Lifetime transitions and window
selection are ordered callback-dependent operations, not SIMD/GPU compute work.

## Build-only evidence

All commands use `build --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'`.

- ProGPU SDK 10.0.201, `src/ProGPU.Tests/ProGPU.Tests.csproj`: final build
  0 warnings, 0 errors, 11.28 seconds.
- Root pinned SDK, `src/ProGPU.Wpf.RealApplicationRunHarness/ProGPU.Wpf.RealApplicationRunHarness.csproj`:
  initial source/harness build 4 warnings, 0 errors, 58.25 seconds; public-host
  fixture rebuild 0 warnings, 0 errors, 6.67 seconds.
- Root pinned SDK, `src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj`: initial full
  bridge fixture build 116 warnings, 0 errors, 17.80 seconds; SDK-gate fixture
  rebuild 20 warnings, 0 errors, 14.42 seconds.

The final source reentrancy guard also rejects nested Run if a callback removes
the current registration. The final source/application-harness rebuild completed
with 4 warnings, 0 errors in 57.42 seconds.
The final bridge fixture rebuild completed with 116 warnings, 0 errors in
15.96 seconds. Warnings are reported, not treated as qualified or suppressed.
No test, verifier, application, VM, GPU or benchmark workload has run, and no CI
checks were polled. PR identity lookup and push are not qualification evidence.

The latest fetched ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`,
an ancestor of the feature branch. Unrelated native edits and performance-artifact
deletions are preserved and excluded from this batch.

## Remaining finish queue

This closes an identified application-lifetime source branch, not the entire
application closure milestone. Native visible handoff and hidden-window behavior,
MVP/Toolkit/third-party application execution, package production, Windows SDK
admission, cross-platform image/input/lifetime/performance comparisons and both
PRs' final required CI remain open. The suspended Windows VM remains a separate
environment blocker; no new resume/reset or configuration change was attempted.
Continue only concrete core startup/package/application blockers before freeze.

See the [shared ProGPU contract and primary-source record](../external/ProGPU/docs/native-mil-application-lifetime.md)
and [core delivery queue](../docs/native-mil-core-delivery.md).
