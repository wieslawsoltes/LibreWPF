# WPF modal desktop acceptance

This desktop workload is still unqualified. `qualified` remains false in source
observations and driver evidence. Hosted Windows runs and retained failures are
described below; local shared VMs remain untouched. The original source-only
MessageBox and popup-dismissal gates remain unchanged.

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
launch, the hosted modal desktop setup observes all three known overlays but
sends at most one normal close request to the account window. Every known window
requires its exact native class, Windows system image path, process and interactive
session. Unknown foregrounds, duplicate/replaced identities and a reappearing
account window fail setup. Setup sends no input and stays inside eight seconds.

On 2026-10-16 the owner explicitly approved a bounded native Escape extension
for Search and Start on the disposable GitHub runner. It is opt-in, restricted
to the first Microsoft reference startup, and sends at most one Escape to each
verified actual foreground shell window: two keys across the entire pair. The
original immutable Forms helper retains foreground-PID and held-input checks and
partial-key cleanup. The guard rechecks actual HWND, class, image, session and
Default desktop, counts each key inside the original 64 actions, and permits no
retry after refusal or reappearance. Its three-second bound, including fresh
source-event verification, is inside the original 60-second deadline. Original
owner text, selection and input counters must remain unchanged; no key may leak
to the application. The portable run has no shell-input extension. Original
caption, scenario, image and cleanup checks follow unchanged. No local VM is used.

Raw images, all original failures, source/payload hashes and package provenance
are retained even on failure. Nineteen pure modal controls, 58 shared Gallery
controls, nine desktop setup controls and eight shell Escape controls pass locally.
The original eleven modal controls remain unchanged. New controls reject foreign
or replaced shell identities, missing approval, local/self-hosted use, refusal,
reappearance, expired deadlines and leaked source keys; they also verify original
action accounting and fresh post-input observations. The Gallery setup is unchanged.
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
The next modal-only setup attempted normal WM_CLOSE for those two exactly
identified shell overlays as well as the account startup window. All five
unsuccessful hosted attempts remain retained; this is a diagnosed environment
repair, not application acceptance or a relaxed foreground/caption check.
The first overlay-setup run stopped without closing anything because
`EnumWindows` returned the Explorer `Shell_OOBEProxy` but omitted the actual
foreground CoreWindow. [Microsoft documents](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows)
that this enumeration covers only desktop applications on modern Windows. Setup
now uses the actual bounded native Z-order traversal already used by the original
owner-obstruction helper. It retains and ignores only the exact Explorer proxy,
rechecks selected identities and keeps the CoreWindow/system-image admission.
The sixth failure and its complete original enumerator output are preserved;
that output did not include the actual foreground CoreWindow.
The seventh setup run showed the same omission in the desktop-app Z-order
chain, with zero close requests. The observer now supplements that chain with
exact native CoreWindow class/title lookups and the independently returned
foreground HWND. These are observed handles, not inferred identities; every
close still requires the exact process image, session, class and unchanged
window metadata. All seven earlier failures remain retained.
The eighth run identified all three actual CoreWindows and exact system images.
The account closed normally, but Search retained WS_VISIBLE after WM_CLOSE;
setup stopped before either application launched, with zero input. That failure
is preserved. It prompted the separately approved Escape guard described above,
without weakening the original caption or foreground admission.
Hosted execution and independent raw-image review remain pending. Source
`qualified=false` values remain unchanged; even a completed Windows pair does
not qualify Linux or close the broader popup/modality issues.

The ninth run successfully dismissed Search using one approved Escape, verified
unchanged owner input counters, activated the exact native caption and captured
all seven original Microsoft reference phases. It then failed the original
final native-window-count assertion after source tooltip closure. The portable
half did not run. All snapshots, images, actions and the original failure remain
retained. A bounded read-only diagnostic now records the exact inventory at that
assertion and one second afterward, within the original deadline. It freezes the
first assertion result: later disappearance cannot convert a failure to success.
No application, input, popup animation, timeout or acceptance predicate changes.

The tenth run completed the original Microsoft scenario with 22 actions, normal
exit and no cleanup errors. Its first native-retirement assertion passed; this
does not explain the ninth run or make that failed attempt successful. The
portable launch then exposed a controller lifetime defect: the unchanged Forms
helper calls process-wide SetProcessDpiAwarenessContext in its constructor, which
cannot be initialized a second time in that same controller process. The pair
now shares the helper's stateless native API bindings after the first successful
run, while retaining separate application processes, run IDs, original deadlines,
source observations and action budgets. The first initialization remains inside
the first original deadline, and the immutable helper is not patched. A pure
paired-run control verifies one initialization and two independent sessions.
The earlier reference context capture also lacks visibly painted Context action
despite an open source/native window; raw-image qualification remains unresolved.

The eleventh run again completed the Microsoft protocol and then reached the
public LibreWPF MessageBox. It failed the unchanged exact source/native client
frame comparison. The portable snapshots and original first owner image remain
retained. The comparison now writes both exact inventories before raising the
same failure, including native input-enabled state; no bounds are fitted, no
window is filtered, and no new tolerance or input is admitted. Local controls
retain rejection of whole-pixel and subpixel differences. Product repair awaits
that actual native evidence.

The twelfth run retained the precise mismatch. The native MessageBox client was
`(240,275,504,151)`, while the old observer reported the content Grid's
`(264,297,456,111)`: exactly its original `Margin="24,22,24,18"`. Content geometry
is not client geometry. Observer schema v2 reads the original layout slot of the
Window's sole visual child, which `Window.ArrangeOverride` allocates after native
frame subtraction, and maps its original endpoints with Window.PointToScreen.
It does not add inferred margins, copy native bounds or change the exact native
comparison. Popup-root and action-target geometry keep their original paths.

The same receipt showed native owner input disabled while its requested controller
enabled intent remained true, as the source contract requires. The observer now
records that intent and the actual source PortableModalInputScope policy separately;
their conjunction is source input admission. Windows acceptance additionally
requires independent IsWindowEnabled=false during modality and true after return.
Application IsEnabled, modal scope, rendering, actions, deadlines and public
preview.69 packages are unchanged. Both Microsoft and portable applications use
the same revised observer. Preparation retains all original source bytes beside
the consumer, records both observer hashes and rejects any other source mutation.
The original manifest and failed receipts are not rewritten or qualified.

Two extra PID-owned blank windows also appeared in that original receipt. A new
read-only inventory records actual native class, owner, styles and front-to-owner
Z order before the existing blocked-owner assertion. No window is filtered or
classified from its dimensions; the original obstruction and retirement checks
remain unchanged pending actual native evidence.

The thirteenth run passed exact client matching in the original Microsoft pair
and public portable MessageBox. Its next failure identified both additional
windows as actual `SysShadow` surfaces (style `0x94000000`, extended style
`0x800a0`, no owner HWND), behind the original owner in independent native Z order.
The old unordered PID inventory therefore incorrectly treated the owner's shadow
as an obstruction. The blocked-owner preflight now uses the actual complete
front-to-owner inventory, including foreign windows; the immutable input helper
still repeats its own native obstruction proof before moving and before clicking.
No class or window is filtered from capture or lifetime evidence.

Hosted retirement now requires exact restoration of the complete initial native
inventory, including each original shadow's HWND, class, styles, owner, enabled
state and rectangles. Initial additional windows are accepted only with the
observed transparent system-shadow contract and proven placement behind the owner;
unknown windows are rejected. Missing, replaced, changed or additional final
windows fail immediately. The original single-window predicate is still recorded
separately, and all previous failures are preserved. The existing bounded failure
diagnostic cannot turn later disappearance into a pass. This repairs an observer
assumption; it does not establish product pixel or cross-platform qualification.
