# Slider drag dispatcher layout

Issue [#52](https://github.com/wieslawsoltes/LibreWPF/issues/52) reports that
WPFGallery Slider dragging jumps while track clicks work. The earlier queued-input
fix covered callbacks arriving off the WPF dispatcher. The source registrar
deliberately returns `false` from `TryBeginInvokeInput` on the owning dispatcher;
that direct route still processed consecutive moves without draining layout.

`Thumb` measures each drag delta from its current local pointer position. After
`Slider` changes its value, `Track` must arrange the thumb before the next native
move. A coalesced render request alone does not establish this boundary. The
bridge now drains Render-priority work after direct pressed-pointer input, using
the existing guarded dispatcher service. MouseUp remains counted until the
existing finally releases its button. Queued-input behavior and passive-move
batching are unchanged; neither Slider nor Thumb is rewritten.

## Controlled source evidence

On macOS, a real source Window, Slider, Track and Thumb received typed host input
through the production activation bridge, with no native window or renderer.
For a 200-DIP track, a 20-DIP thumb, range 0–100 and initial value 20, six 9-DIP
moves independently require values **25, 30, 35, 40, 45, 50**.

The freshly compiled pre-fix main bridge produced **25, 35, 50, 70, 95, 100**,
with stale Track arrangement. The same baseline with an explicit layout boundary
between moves produced the expected sequence. The freshly compiled fixed bridge
produced the expected sequence without manual layout or dispatcher pumping
between events. Consumer bridge bytes were compared with the selected build
output, avoiding a late package runtime-copy substitution.

`eng/SliderDragContract/Program.cs` extends this paired control to ten independent
processes: horizontal/vertical, normal/reversed, 100%/200% DPI, and a snapped
500–1000 range at both DPI scales. Every forward and reverse value, valid Track
arrangement, actual Thumb capture/release and absent native window is asserted.
Only initial setup calls `UpdateLayout`. Each process owns its source Window
lifetime and headless Show registration; registrations do not escape into the
existing application smoke process. All ten passed against the freshly compiled
fixed bridge; the unchanged fixture rejects the verified pre-fix bridge on its
first stale-layout event.

The SDK external smoke harness builds and runs this same fixture against its
fresh local package feed, with a 30-second deadline per child. Three typed bridge
tests independently assert exact direct pressed-move/MouseUp ordering, passive
batching and deactivation cleanup. The local activation suite passes 84 tests
without skips. Full fresh-source package CI is a separate required gate.

## Qualification boundary

These are real-control/source-input contracts, not synthetic DragDelta events,
native pointer-device execution, WPFGallery screenshot comparison, renderer
qualification or proof of Ubuntu application parity. Existing native/application
acceptance gates remain required and unchanged. No VM was started for this work.
