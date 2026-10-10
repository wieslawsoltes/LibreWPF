# Portable MessageBox content overflow

The portable source dialog bounds its height, but previously placed wrapped
message content directly in the bounded grid row. A long message could extend
beyond that row without exposing any way to reach its ending.

Keep the message area in a pixel-scrolling `ScrollViewer`, with automatic
vertical scrolling and horizontal scrolling disabled so the original text still
wraps. The icon, original message, alignment and right-to-left policy stay in
the message area. Keep the action buttons in the separate auto-sized final row;
scrolling must not move those buttons out of reach.

The existing source-dialog test covers short and 80-line messages through both
explicit-owner and inferred-owner `MessageBox.Show` calls. It inspects the actual
dialog controls, retained message text, scrolling policy and fixed button row.
It lays out the actual dialog content at the bounded size, using ScrollViewer's
real default control template, and requires overflow only for the long message,
scrolls to the bottom, and verifies the action button remains visible and in the
same position before clicking it. Existing owner-input suppression,
dialog cleanup, close-only resize intent and hidden-taskbar assertions remain.
The existing required source MessageBox CI gate runs this test without a new
skip, relaxed assertion or longer deadline.

This content change affects the WPF-rendered portable dialog in both managed and
native renderer configurations. The later source-owned backend selection also
uses that dialog for portable Windows hosts; Windows-MIL retains its native
user32 MessageBox route. See [backend selection](portable-messagebox-backend-selection.md).
Process-backed startup/service dialogs are unchanged. No renderer, shader or
native ABI change is required: scrolling uses the existing source ScrollViewer.
Source layout assertions do not qualify native window chrome, rendered clipping,
mouse-wheel or keyboard scrolling. Final Linux/macOS desktop interaction and
the Windows native-reference checks remain required for popup parity.

Local Release evidence on macOS ARM64: the new regression failed on the original
dialog (`ScrollViewer` expected, `Grid` observed), while the other seven source
gate tests passed. After the implementation, all eight gate tests pass without
skips, including all four short/long and explicit/inferred-owner dialog paths.
The source-only host does not supply a native window theme, so the regression
measures and arranges the actual content grid directly; it does not substitute
its ScrollViewer template, text formatter, scroll offsets or button controls.
That historical source evidence predates the portable-Windows selection change;
the newly authored Windows policy and resolved-owner configurations are unrun.
