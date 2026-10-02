# WPF modal desktop acceptance

This is an authored desktop workload, not a passing receipt. `qualified` remains
false in source observations and driver evidence. No build, test, syntax check,
VM, UI, native execution, CI dispatch or staging was performed for this branch.
The original source-only MessageBox and popup-dismissal gates remain unchanged.

`ProGPU.Wpf.ModalInteractionApp` uses the real SDK bootstrap. The Windows project
links the exact same application and observer source against Microsoft WPF.
Windows/Linux retain their existing defaults; macOS requires the existing explicit
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

Desktop driver and authored negative controls are the next part of this same
implementation; final Windows Microsoft/portable, macOS native-session, and
Linux default execution and package/application qualification remain required.
