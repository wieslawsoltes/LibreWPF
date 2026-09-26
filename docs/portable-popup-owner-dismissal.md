# Portable popup owner deactivation

Acceptance action: open an ordinary Showcase/Toolkit popup, ContextMenu or
ToolTip, then deactivate its actual owning window. The nonactivating popup
must not leave an auto-close menu or mouse capture active after that transition.

The checked-in Windows `Popup.PopupFilterMessage` handles `WM_ACTIVATEAPP` by
scheduling `HandleDeactivateApp` at Normal dispatcher priority. That method
closes `StaysOpen=false` popups and raises `PopupCouldClose`; the real ContextMenu
and ToolTip consumers own their corresponding close behavior. Portable sources
have no such HWND message hook. Their mouse provider deliberately retains capture
when input moves between owner and popup sources, so provider deactivation alone
cannot substitute for this dismissal notification.

The portable path now observes `Deactivated` on the actual retained owner Window
while the popup is shown and schedules the existing dismissal method at the same
priority. It does not use a mutable PlacementTarget to select that owner. Closing,
destroying or a rejected Show detaches the event and cancels pending work; a
notification from an earlier opening cannot close a reopened popup. `StaysOpen`
and `PopupCouldClose` retain their existing policies. Ordinary keyboard focus and
pointer movement into a popup do not change Window activation and do not trigger
this hook. Native popup creation remains explicitly owned and nonactivating; this
patch does not repair or conceal a native provider that violates that contract.

The twelve authored source cases use real Window, Popup, ContextMenu and ToolTip
instances with the existing typed recording host. They cover explicit/ownerless
ownership, deferred dismissal, capture release, StaysOpen, repeated activation
notifications, pointer/focus entry, close/reopen with source reuse or destruction,
changed PlacementTarget, nested popups, and failed Show cleanup. The source CI
gate runs these cases from the original PresentationFramework.Tests assembly,
requires all twelve, rejects skips and retains its 60-second deadline.

Local C# compilation and execution are deferred to CI during the implementation
batch. Shell syntax/diff checks do not qualify these authored cases. Actual
Windows reference and portable acceptance remains required on each admitted
native platform: entering a popup/submenu must retain owner activation; switching
to another application must dismiss it and release capture; reopen and nested
menus must remain usable. Placement/DPI/work-area geometry, native focus/capture,
screenshots, package bytes, Cocoa/X11/Wayland support and broad UI parity are not
qualified by this source-only change. No renderer, shared ProGPU API or platform
admission is changed.
