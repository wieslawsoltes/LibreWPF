# Source outer size after X11 mapping

The original Toolkit/AvalonDock application on the qualified `a255b4cc` package
closure passed its headless X11 GL gate but failed the same gate under GNOME
Wayland/Xwayland. Its declared outer size was 980 by 640. The client remained
980 by 640 after the window manager supplied a 39-pixel top frame. The original
source assertion rejected that mismatch; it is preserved.

Evidence: [hosted run 39106469170](https://github.com/wieslawsoltes/LibreWPF/actions/runs/39106469170),
artifact `12129110152`, SHA256
`d38258660de5725e4fd7a026ae1bebbefa35f63e2a665e465d8acb6c1638504c`.
Headless validation completed in 36.849 seconds; the mixed-session process failed
in 11.219 seconds. Both selected actual OpenGL and retained identical payloads.

Initial source sizing happens against the hidden native window. X11 frame
measurements can change when that window is mapped. The native show boundary now
notifies its source only after the visibility operation returns and the same
window remains visible and live. The X11 source adapter reads one actual frame
for both axes and reapplies the source outer dimensions to the client. Explicit
Width/Height retain precedence over the actual SizeToContent result. An axis
without a source dimension retains its existing client size. Empty frames remain
empty; unavailable or invalid measurements do not invent decoration sizes.

The correction runs at Show, not on every render or timer tick. Maximized and
minimized states keep their existing window-manager sizing. Other native
providers keep their sizing behavior. Reentrant hide, close, disposal or native
window replacement cannot publish a stale show notification. No activation,
pointer input, framebuffer-DPI inference or source Width/Height rewrite is added.

Unit controls cover post-show sizing, two-axis frames, SizeToContent dimensions,
unknown/empty/invalid frames, non-normal states, original show failures and
reentrant visibility changes. They do not qualify native execution. The original
Toolkit source/assertions and 180-second limit must pass with the rebuilt whole
producer packages in both environments. Full transparent-pixel and physical
docking/input acceptance remain independent gates under #187.
