# Source X11 taskbar visibility

Portable source `Window.ShowInTaskbar` previously reached only the Win32 host.
The source now carries an independent optional X11 callback and desired value to
the existing `SilkWindowController.SetShowInTaskbar` implementation. No ProGPU
dependency or package pin changes are required for that existing operation.

An explicit source value survives creation and hide/show. After a successful
native Show, the exact live X11 client sends the existing
`_NET_WM_STATE_SKIP_TASKBAR` add/remove request. Live property changes use the
same operation. Hidden or unavailable clients keep the desired value for their
next Show and report no applied request. Unselected generic hosts do not gain
a new taskbar policy. Windows retains its separate hidden-owner/icon policy;
Cocoa and other providers are not admitted by this change.

Visibility reads and requests retain the original window, controller, creating
thread and source-intent generation. Reentrant hide, replacement, retirement or
intent changes reject a stale request/result. Clearing or replacing the source
registration removes the optional callback; the legacy registration signature
and default behavior remain intact.

The existing X11 operation sends an advisory client message. Its successful
return means the request was sent, not that the window manager honored it or
that a taskbar icon was visually observed. The source property and window type,
owner, input and modal state remain independent. In particular, this does not
change MessageBox's explicit `ShowInTaskbar=false` into a requested extra icon.
The [EWMH state protocol](https://specifications.freedesktop.org/wm/1.5/ar01s05.html)
distinguishes mapped-window requests from the state property read on mapping;
this adapter uses the existing mapped-window request after Show.

All 13 focused host controls and 20 source modal/caption/taskbar controls pass
locally without skips. Four source/test assembly pairs have identical hashes.
These controls exercise initial false, live changes,
hide/show, callback clearing, advisory failure, stale observations and retirement
without native input. Actual Linux taskbar appearance, owner-input blocking,
focus restoration and the final qualified package/application remain separate
acceptance gates for #113.
