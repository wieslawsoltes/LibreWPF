# WPF modal desktop acceptance

This is an authored desktop workload, not a passing receipt. `qualified` remains
false in source observations and driver evidence. No build, test, syntax check,
VM, UI, native execution, CI dispatch or staging was performed for this branch.
The original source-only MessageBox and popup-dismissal gates remain unchanged.

`ProGPU.Wpf.ModalInteractionApp` uses the real SDK bootstrap. The Windows project
links the exact same application and observer source against Microsoft WPF.
The acceptance project explicitly selects existing `NativeMilWgpu` rendering and
native MIL hit testing; the observer rejects another actual host mode. Windows/Linux
retain their existing modal/provider defaults; macOS requires the existing explicit
`--libre-native-modal-sessions` startup selection. No source/provider default or
qualified submodule pin is changed. Rejected native startup is not replaced by
another renderer, popup surface or modal policy.

Real UI button actions invoke `MessageBox.Show(owner, ...)` and `Window.ShowDialog`.
The source scenario also contains real Popup, ContextMenu, ComboBox and ToolTip
controls. An exposed owner guard button allows a desktop driver to attempt owner
input during each modal operation without clicking through the dialog. Source
observations record original events, text, focus, window identities and immutable
sequence files. They never invoke synthetic source input, assign focus, or claim
that source `IsEnabled` alone proves blocked desktop input.

The diagnostic seam reads only already-existing host/native controllers and
source-owned popup hosts on their creating thread. It does not pump, create,
present, show or acquire an input lease. Cocoa geometry carries the actual native
window, content-view and CG window number, separately from source handles and
framebuffer scale. Unsupported geometry/identity is an explicit missing receipt,
not inferred from an opaque handle or replaced with a source-bound rectangle.

`eng/progpu-wpf-modal-desktop.py` launches an original Microsoft process followed
by a portable process on Windows; it launches only portable on macOS and X11.
Both Windows projects use identical application/observer files. Microsoft popup
identities come from the actual live `HwndSource` for Popup, ContextMenu, ComboBox
and ToolTip, not a fabricated window around a control. The desktop observer
independently matches PID, title, HWND/CG window number/XID and exact client bounds.
It does not fit source bounds to a screenshot or infer a missing Cocoa provider.

The owner guard remains outside the centered dialog. Before a physical blocked
owner click, both the source-layout check and independent front-to-back native
inventory must prove the target unobstructed. The helper checks again after the
real pointer move. No click is admitted based on `IsEnabled` alone, or through a
covering dialog/foreign window. The driver retains source owner event/text/selection
invariance while the modal phase is active, then requires real return/focus/input
restoration and a successful physical owner button click. Popup outside dismissal,
ContextMenu Escape, ComboBox selection and ToolTip open/close use ordinary OS input.
Source code never synthesizes a Click, changes focus or pumps a test-only loop.

The shared Forms helper must be provided as an **explicit immutable checkout** via
`--forms-helper-checkout` and `--forms-helper-commit` (full 40-character commit).
The driver compares each imported Python/Swift file with `git show` of that exact
commit and records hashes. It requires `blocked_owner_pointer`; the older Forms
driver lacking that boundary cannot stand in. No gitlink or package cache is changed.
The authored counterpart is Forms draft #154, exact source helper commit
`8858fb8a3c5066f866e8a39ab21b57d0365039cd` (stacked on #152). This is an explicit
driver-source dependency, not a qualified runtime or a change to WPF's Forms pin.
The macOS helper is an explicitly supplied source-matched binary, requiring normal
Accessibility and Screen Recording permissions; its hash, preflight and unresolved
binary/source-build provenance are retained, never silently elevated to proof.

Final validation invocation shape (not executed here):

```text
python eng/progpu-wpf-modal-desktop.py --platform windows \
  --reference-app ABSOLUTE_ORIGINAL_APPHOST --portable-app ABSOLUTE_PORTABLE_APPHOST \
  --forms-helper-checkout ABSOLUTE_IMMUTABLE_CHECKOUT --forms-helper-commit FULL_COMMIT \
  --output NEW_ABSOLUTE_EVIDENCE_DIRECTORY
```

Use `--platform macos --macos-helper ABSOLUTE_NATIVE_HELPER` without a reference
app for macOS, or `--platform x11` without a reference app for Linux. Wayland is
not substituted for X11. The driver adds the native-modal source startup option
only on macOS; unsupported startup, geometry, provider or input is a failure.

Each process retains the original absolute 60-second desktop deadline, including
native-helper setup, at most 650 source snapshots, 64 physical actions, 256 KiB per
JSON receipt, 16 MiB per image/128 MiB aggregate and bounded owned-process cleanup.
All evidence files are new; symlinks/reparse paths, duplicate JSON/options, mutable
helper inputs and identity changes fail closed. The source captures real event
and presentation-frame observations, not GPU completion or usable-pixel assertions.
Seven captured scenarios and successful process exit still leave `qualified=false`.
App/source hashes do not establish package or binary/source identity; final producer
provenance, independent image comparisons and actual cross-platform runs remain gates.

`eng/tests/test_wpf_modal_desktop_contract.py` authors pure negative/positive controls
for receipt identity, immutable writes, malformed metadata, exposed source bounds,
owner event invariance, stable geometry, original deadline and immutable helper head.
These controls have **not been executed**, and do not replace the native obstruction,
physical event, rendering, focus/lifetime or original Microsoft comparisons.
