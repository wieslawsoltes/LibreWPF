# Source-owned MessageBox backend selection

This child of LibreWPF #252 addresses a concrete Windows boundary in issue #113.
`WindowInteropHelper.Handle` for a portable Window is the managed presentation
identity allocated by `PortablePresentationSource`, not its native HWND. The
former Windows-only branch passed that identity directly to user32 MessageBox.

MessageBox now selects the portable route before `GetActiveWindow` or user32 when
the actual owner has a portable activation, portable windowing is registered, or
the existing configured/frozen media choice is Portable. Non-Windows keeps its
portable route. Reading the existing choice does not reset or replace it. An
ordinary Windows-MIL owner with no portable selection retains the original native
MessageBox path, default active-HWND lookup and native style/result conversion.

The portable route resolves a supplied presentation identity through the same
Window resolver used by portable WindowInteropHelper ownership. It requires one
live, same-dispatcher PortablePresentationSource whose actual Window/root/handle
identities agree; it does not accept the public HwndSource facade as an HWND.
Unknown, detached, disposed, mismatched or foreign portable owners cannot fall
through to native MessageBox. Explicit source Window owners retain their identity
and dispatcher checks without manufacturing a native handle. Enum/options and
service-owner validation precede modal admission and callbacks.

The existing order remains explicit service override, actual WPF source dialog,
then startup/service fallback. Registrations may precede portable startup on
Windows; their invocation remains gated by source/backend selection. Ownerless
dialogs continue to resolve the actual active/visible application Window. Hidden
explicit owners and calls without an Application/portable host retain the service
fallback route, not a synthetic source Window. ServiceNotification and
DefaultDesktopOnly retain their existing owner restriction and service route.

The WPF dialog uses the existing Window.ShowDialog implementation: source input
admission before Show, the source-owned modal loop, exact native release, then
gate/focus restoration. This does not enable automatic Cocoa sessions, change
popup/provider selection or broaden native nonclient suppression. It adds no
producer/neutral API and leaves the qualified ProGPU and LibreWinForms gitlinks
unchanged.

## Authored controls, not execution evidence

The existing isolated PresentationFramework modal fixture retains its original
four short/long × explicit/inferred-owner configurations and adds two matching
resolved-presentation-identity configurations. All six retain actual generated
dialog controls, source input/drop/activation rejection, scrolling, dialog result,
release and restored-owner assertions. The same source fixture now admits portable
Windows; the separate Windows-MIL policy remains outside this portable lane.

Two service-override calls, four preflight rejection cases (invalid enum,
service owner, foreign handle, detached identity), and one hidden-owner startup
fallback are authored in that same isolated process. Callback/dialog counts must
remain unchanged on rejected requests. The live source dialogs must take priority
over a registered fallback. The existing source-graph control now asserts portable
selection and completion before active-HWND lookup/user32, rather than requiring
the faulty operating-system-only branch.

The existing 20-second source-thread, 40-second child-process and 60-second gate
deadlines are unchanged. No build, test, syntax check, verifier, probe, native/UI,
VM or CI execution was performed. Final combined-tip compilation/execution and
real Microsoft-versus-portable Windows plus Linux/macOS modal desktop input,
focus, native chrome and retirement qualification remain required. These source
controls alone do not close issue #113.
