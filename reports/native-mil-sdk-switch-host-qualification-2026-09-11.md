# SDK switch recording-host qualification

The d669c28c0 integrated native gate passed source application/lifetime/theme,
package byte audits, bundle provenance verification, extracted SDK/Avalonia
consumer smokes, and the SDK switch/mixed desktop builds. It stopped in the SDK
switch runtime harness at an outdated 17-argument reflection registration call.

The current source registration has 22 arguments. The package-isolated harness
now supplies explicit nulls for the five optional capabilities it does not provide:
hidden creation, system menu, dialog loop, owner updates and dialog release.
No capability is advertised or product guard relaxed. The harness rebuilds with
zero warnings/errors and progresses beyond registration.

The next runtime failure is tooltip opening: the recording host has no portable
popup host registered. The source correctly rejects an unhosted popup. Connect
the existing popup service in this harness and retain its original tooltip and
application assertions; do not remove the rejection or bypass the action.
Full SDK qualification, final CI and platform gates remain open.

The recorder now binds its original presentation source to the packaged ProGPU
host, explicitly selecting the existing owner-surface popup factory seam because
this fixture has no native window. It restores the factory and disposes the host
before the borrowed source. Tooltip/context-menu assertions remain unchanged;
this does not qualify native popup monitor/ownership behavior.

After interaction checks, the recorder closes its actual Window and flushes
application work before returning from the host loop. This satisfies source-owned
OnLastWindowClose policy instead of relying on the removed implicit shutdown.
The full SDK switch runtime harness now passes, including its original close,
dispose and application lifetime assertions. Build: zero warnings/errors, 2.93s.
The focused run uses the d669c28c0 packages and b54db165 ProGPU feed without
development native loader paths. Whole-gate final-revision validation remains due.
