# Source Win32 taskbar ownership

The original Microsoft WPF reference (Actions run `39028587600`) observed nine
source/native transitions. `ShowInTaskbar=false` clears only `WS_EX_APPWINDOW`;
it does not add `WS_EX_TOOLWINDOW` or remove ordinary caption/system-menu chrome.
An unowned source window uses a retained hidden top-level owner. WPF creates
that dependency even when an explicit source owner is present, reuses it across
true/false and owner changes, and destroys it only when the subject closes.

The source snapshot carries the raw property through an optional typed host
callback. Only actual Win32 hosts install it. Generic hosts without explicit
source taskbar metadata retain their existing policy. This change does not admit
Cocoa, X11 or mixed Wayland/Xlib taskbar behavior.

The native policy preserves all unrelated style bits and source ownership. It
hides and restores a visible child without activation around owner/style changes,
then verifies the exact requested native state. Failure attempts to restore the
original owner, style and visibility while retaining the original exception and
any rollback failure. The hidden HWND has an exact per-host identity and belongs
to the child's process and creating thread. Owner chains reject cycles and foreign
windows before mutation.

An already matching owner is a native no-op after identity validation. The first
actual x64/ARM64 API controls (`39032667642`) exposed Windows rejecting an
unowned-to-unowned owner write with error 1400 before the first transition. Those
failures are retained. The correction preserves the complete original control,
including its initial idempotent owner assignment and deadlines.

The hidden owner receives owned copies of the child's current large/small icons,
including actual registered-class icons when WM_GETICON returns zero. This follows
the documented [WM_GETICON lookup](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-geticon).
The source provider's icon handles remain borrowed. Icon changes and clears update
the hidden owner; replaced copies retire only after reading its actual bindings.
Partial publication attempts rollback and retains any still-bound copies. Failed
releases stay owned for retry. The source's existing icon decoding and default
application-icon policy are separate from this ownership correction.

Retirement completes renderer cleanup, destroys the actual native child, destroys
the hidden owner, then releases its icon copies. A later failure does not repeat
successful child/owner destruction. Creating-thread and reentry guards remain in
force, and failed dependency cleanup keeps the host retirement pending.

Validation currently includes 542 focused host, source activation, taskbar and
icon controls with zero skips, plus two actual source property/registration cases
and eight original source modal controls. The broader source activation-class
attempt retained one native-Windows-only FAIL_SKIP on macOS; it is not a complete
native class pass. Original Windows paired transitions, full producer/package CI
and application acceptance remain required. The authored ownership controls and
the Microsoft-only reference do not qualify this implementation for release.

The unchanged native API control passes on both Windows x64 and ARM64 after the
idempotent-owner correction (run `39032979587`, product `399e96d91`). It preserves
all nine transitions, copied-icon replacement, borrowed provider icons and actual
child/owner/icon retirement within the original 20/30-second bounds. The whole
Build retains this native control in its Windows runtime job and the managed
ownership controls in its existing retained-invalidation job. These API results
remain distinct from the source-application and package acceptance above.
