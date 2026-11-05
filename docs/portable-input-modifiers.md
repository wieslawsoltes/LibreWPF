# Event-time input modifiers

Portable source input already transports Shift, Control and Alt with each event.
`PortableWindowActivationService` now scopes that aggregate over the complete
synchronous input report, including capture routing and routed source callbacks.
`Keyboard.Modifiers` therefore observes the event snapshot, not a keyboard cache
that may have changed while a native pointer event waited for dispatch.

Nested pointer, keyboard and committed-text events each select their own snapshot.
Unwinding restores the outer snapshot, including after exceptions. It never rolls
back the physical key cache: a real key-up delivered during a pointer callback
stays released afterwards. Aggregate flags do not establish left/right key
identity or replace individual key/toggle queries. Windows/Super remains excluded
from `Keyboard.Modifiers`, matching the existing native source policy; the host's
Command-to-Control translation remains separate.

The stack-only scopes allocate no per-event heap object, require the source
thread, and reject out-of-order or stale release. Unknown aggregate flags fail
before scope publication. Native Windows devices retain their original modifier
query, and public API signatures do not change.

`eng/progpu-wpf-input-modifiers-source.sh` runs actual routed pointer, wheel,
nested key/text and exception tests, then the device-level cache/lifetime tests.
It runs in the existing Linux source-contract CI job, after the original source
framework test assembly is built. This is source input evidence, not native
Cocoa popup admission, capture cancellation, precise scrolling or application
qualification; those remain separate integration work.

The HwndHost keyboard forwarding and HwndSource mnemonic helpers also select the
frozen input owner before querying modifiers. Portable callbacks use the same
scoped keyboard snapshot, including control-character classification (Control
without Alt, and an actual Unicode control character). Native Windows keeps its
original Shift/Control/Alt queries; text classification keeps its short-circuited
Control/Alt queries and Win32Exception handling. No Win32 shim key-state export or
fabricated physical key state is required. The existing source gate includes
three controls for all eight modifier combinations, nested text callbacks,
exception restoration and a physical key release during an outer event scope.
This fixes the reported keyboard entry point in #307; monitor, module-path and
locale startup contracts in that PR remain independently reviewed work.
