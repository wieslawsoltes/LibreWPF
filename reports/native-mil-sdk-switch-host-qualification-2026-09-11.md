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
