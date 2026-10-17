# Portable native-loop synchronization context

Issue [#281](https://github.com/wieslawsoltes/LibreWPF/issues/281) reproduces with
the reporter's six unchanged files and 17 hash-verified public preview.69
packages. On macOS, a real mouse click entered the handler on thread 1 with no
SynchronizationContext. Its awaited continuation ran on thread 9, failed
Dispatcher.CheckAccess, and threw when assigning Button.Content. The unchanged
application closed normally after 30.610 seconds within its 60-second diagnostic
bound. Original source, package hashes and console output are retained.

Dispatcher operations already install a context around their own callbacks.
The portable native host loop bypassed Dispatcher.PushFrame, so native input
outside those operations inherited the caller's context instead. Application.Run
now owns a source DispatcherSynchronizationContext across initialization, host
handoffs, hostless dispatch and shutdown. ShowDialog owns the same scoped policy
for standalone and nested modal loops. Both restore the exact caller context on
normal return and exceptions. App construction and the native HWND loop retain
their existing behavior.

The real-source Application.Run harness checks an awaited continuation from each
host callback, source dependency-object access, dispatcher-flush restoration and
restoration of an explicit caller context. Its additional context scenario covers
standalone/nested dialogs and exceptions from application and dialog callbacks.
It uses the existing typed recording host and public-source diagnostic loader;
it does not substitute queued callbacks for the reporter's physical click.

On macOS ARM64, the ordered managed-transport, theme and harness builds pass.
All four source lifetime scenarios pass: last-window, main-window, explicit
shutdown with a hostless interval, and dispatcher context with standalone/nested
dialogs and callback exceptions. The original five-second dispatcher flush stays
unchanged. Local execution uses byte-identical harness output staged inside the
checkout because the loader locates source from its physical output directory.

Head `beef4c0b83571448fa5bf897fd66f68f3077fdb1` passed all sixteen jobs of
[Build 38938632765](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38938632765)
and Docs. The unchanged reporter restored seventeen hash-verified packages from
that complete successful producer. One actual mouse click then kept both sides
of the await on thread 1 with DispatcherSynchronizationContext, returned true
from CheckAccess, and visibly changed the button to “done”. The process closed
normally in 24.908630 seconds under the same 60-second limit, with empty stderr
and unchanged source/payload hashes. [#283](https://github.com/wieslawsoltes/LibreWPF/pull/283)
merged with the exact tested tree and closed #281. Modal context restoration does
not qualify native owner blocking, focus or window decorations.
