# Original Windows button positions

The diagnostic original modal run `38957951448` shows the native cursor at the
ShowDialog button `(190,233)` while both source down/up events still use the
preceding guard's client position `(110,438)`. The move to `(110,153)` arrives
after both button events. Thus swapping the source target, slowing the input
helper or repeating the click would conceal the actual ordering defect.

The live source host binds its exact ProGPU `SilkWindowController` when attaching
`SilkNetWpfInputService`. Each ordinary mouse down/up copies the controller's
original native client position only when both phase and button match. That value
is frozen before modifier reads and source callbacks. Existing desktop/DPI
normalization still applies once; native button bookkeeping, unmatched releases,
event order, source modal gates and capture behavior stay intact.

ProGPU provides this typed packet through its existing Win32 procedure. It reads
the original signed message coordinates, not the last mouse move or a later
ambient cursor sample. It is available only on the live controller's creating
thread while that message is being delivered. Nested keyboard/other messages
cannot inherit the outer button packet. This requires the matching qualified
producer and rebuilt consumer; existing qualified submodule pins must not be
advanced to an untested producer just to compile the new adapter.

The bridge's source project references now honor the existing `ProGpuSourceRoot`
property, matching the framework compatibility projects. This keeps an explicitly
selected producer coherent during local authoring instead of combining the new
framework reference with an older hardcoded bridge reference. The default remains
the pinned `external/ProGPU` checkout; a local source build is not qualification.

Other/custom input services keep their existing attachment. Absent or mismatched
packets retain the existing provider policy. Scroll, Cocoa's complete native
pointer stream and synthetic provider releases remain separate. The packet reader
is bound once for the lifetime of the actual input subscription; the adapter
copies its value without per-event allocation, replay or delay. The controller
retains its own creating-thread and live native-identity checks.

Controls deliver down/up before the delayed move for every supported button,
use a deliberately different later cursor, check signed positions and unchanged
event order, and prove that native button delivery never reads that cursor.
Additional controls retain unavailable/mismatched provider behavior and verify
subscription disposal. These controls and the producer's real Windows callback
tests do not qualify the original desktop application. The same original seven
scenarios, captures and 60-second limit remain required after rebuilding packages.
