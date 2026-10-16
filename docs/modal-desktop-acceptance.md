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

## Isolated hosted Windows execution

`Original Windows modal desktop checks` runs the already approved paired workload
on a disposable GitHub-hosted ARM64 Windows desktop, without using local shared
VMs. `eng/WindowsModalDesktop` verifies the original application, observer and
driver against immutable source `0214bb4569553741244c4fdf10cb78a9dc90661f`, and the
shared helper against `1538b491c17d300387339adce7196700f41cf23a`. Both applications
use the original SDK 10.0.201 build configuration; the portable consumer restores
public preview.69 packages whose complete unsigned contents match the published
release assets. The receipt records the actual installed runtime separately.

Preparation does not launch either application. The input stage retains the
original seven-scenario driver protocol: Microsoft first, portable only after
success. A hosted-only startup option reuses the Gallery controller's bounded
caption activation, after the modal observer has independently matched the exact
PID, title, HWND and client geometry. It sends at most one unobstructed native
HTCAPTION click, counts it in the original 64-action budget, and observes actual
foreground ownership inside the original 60-second deadline. It rejects held
input, changed identities, covered captions and failed activation without retry.
The shared Gallery entry still requires its original presented-frame witness;
the modal entry uses its own source/native identity proof. An existing native
observer must prove per-monitor-v2 coordinates before the helper adopts them.
Each process retains all seven scenarios, the 60-second deadline, original
obstruction/identity checks, 64-action budget, captures and owned cleanup. Before
launch, the hosted modal desktop setup accepts the ordinary shell or sends
at most one normal close request each to the observed Microsoft account, Search
and Start overlays. Each requires its exact native class, Windows system image
path, process, window and interactive session. Unknown foregrounds, duplicate or
replaced identities and a reappearing overlay fail setup without retry. Setup is
bounded by eight seconds before either application starts; it sends no input,
changes no settings or clipboard contents, and retains
its own receipt. No failed attempt is retried by the workflow. Raw images, original failures,
source/payload hashes and package provenance are uploaded even on failure.

All fourteen pure modal receipt/deadline/owner-guard controls and all 58 shared
Gallery controller controls pass locally. The original eleven modal cases are
unchanged; additional controls cover the startup connection and action accounting.
Nine additional desktop setup controls verify the actual three-overlay plan,
ordinary-shell no-op, foreign/replaced/duplicate identity rejection, refused
close and reappearance, the distinct Explorer proxy and bounded native Z order.
The existing Gallery setup stays unchanged.
The first hosted run stopped at original-source hash verification before any
application build or execution. Its original job log is retained. Hosted
checkouts now explicitly keep committed LF bytes; the exact source hashes are
unchanged, and preparation failures retain a receipt before propagating.
The next run verified both builds and the complete public package closure, then
failed the Microsoft reference's foreground check before any physical input.
Its original snapshots and result remain retained. Strict hosted desktop
preparation subsequently closed the exactly identified WWAHost startup window,
but the original controller still failed foreground ownership before input.
That third result is also retained. The explicit caption option replaces only
the initial activation request; original application and Forms helper bytes,
scenario actions, foreground checks and all deadlines remain unchanged. The
historical driver is retained beside the selected controller, whose exact
source hashes and checkout commit are recorded before and after execution.
The fourth run reached the guarded caption setup but found no unobstructed
owned caption point, before sending any input. Its source snapshots, controller
failure and zero-click receipt remain retained. Caption diagnostics now record
the native window bounds, client origin, actual foreground identity and each
candidate's native point owner/hit result without changing admission. The one
strict desktop setup step runs after package preparation, immediately before
launch, so its observation cannot become stale during the two-minute build.
That diagnostic run identified a foreground Search window and a full-desktop
Start window covering all three original caption candidates. No input was sent.
The modal-only setup now closes those two exactly identified shell overlays as
well as the account startup window, using normal WM_CLOSE only. All five
unsuccessful hosted attempts remain retained; this is a diagnosed environment
repair, not application acceptance or a relaxed foreground/caption check.
The first overlay-setup run stopped without closing anything because
`EnumWindows` returned the Explorer `Shell_OOBEProxy` but omitted the actual
foreground CoreWindow. [Microsoft documents](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows)
that this enumeration covers only desktop applications on modern Windows. Setup
now uses the actual bounded native Z-order traversal already used by the original
owner-obstruction helper. It retains and ignores only the exact Explorer proxy,
rechecks selected identities and keeps the CoreWindow/system-image admission.
The sixth failure and its original complete desktop inventory are preserved.
The seventh setup run showed the same omission in the desktop-app Z-order
chain, with zero close requests. The observer now supplements that chain with
exact native CoreWindow class/title lookups and the independently returned
foreground HWND. These are observed handles, not inferred identities; every
close still requires the exact process image, session, class and unchanged
window metadata. All seven earlier failures remain retained.
Hosted execution and independent raw-image review remain pending. Source
`qualified=false` values remain unchanged; even a completed Windows pair does
not qualify Linux or close the broader popup/modality issues.
