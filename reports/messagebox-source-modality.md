# Public MessageBox source modality

Issue [#113](https://github.com/wieslawsoltes/LibreWPF/issues/113) reported the
WPF Gallery MessageBox sample as nonmodal on preview.45 and requested a WPF dialog
instead of the process-backed dialog. The current non-Windows source route already
tries `PortableMessageBoxDialog` before a registered fallback. That implementation
creates a real `Window`, resolves its owner, and calls `Window.ShowDialog`.

`PortableMessageBoxModalTests` protects that public route without a MessageBox
override, replacement dialog, or `ShowDialog` override. In a child test process it
creates an actual `Application`, `Window`, and portable presentation sources. The
existing typed source-host callbacks provide the dispatcher pump and lifetime;
they do not replace WPF's modal scope or result handling. A scheduled dispatcher
callback checks the actual dialog, owner, enabled intent, thread-modal state,
blocked owner activation, input and drag/drop, then clicks the actual generated
default **No** button. Both explicit-owner and ownerless/main-window calls must
return `No`, close and dispose their dialog, and restore owner admission and events.

The input assertions are deliberately separated: `TryActivateInputOwner` must
reject before invoking the registered activation callback; a synchronous
`ProcessInput` call for the owner's real source must not reach the public
`InputManager.PreProcessInput` event while modal and must reach it after close;
`ProcessDragDropEvent` must suppress then restore the real owner's routed `Drop`
handler. The source input check does not fabricate renderer hit selection or
claim that `Window.Activate` independently enforces a native host's policy.

Process isolation is necessary because `Application` creation and shutdown are
process-global. The child and outer invocation are bounded, and the dedicated
gate fails on skips or missing tests. The source test project explicitly references
the actual `UIAutomationProvider` dependency needed by modal focus preparation.

Run `bash eng/progpu-wpf-messagebox-modal.sh` with a compatible .NET SDK. The script
first compiles the source test graph, then runs this class and the existing dialog
lifecycle and backdrop source contracts under the portable media backend. The
existing Linux canonical source integration job invokes it without changing its
deadline or removing other checks.

Local validation on macOS ARM64 used the source graph pinned by main
`5e261dd478db474cd8d0e60288ccda63ab578bb0` and its exact ProGPU gitlink
`b622c4b0f106d003c9259a0b24abeba8c54773db`. The complete source test project compiled
with zero errors and five existing warnings. Under .NET 10.0.5 the new public-path
test passed with zero skips, including both owner cases; a combined invocation
with the six existing dialog lifecycle tests passed **7/7, zero skips**. No native
window, emulator, or VM was started for this validation. Exact-head CI remains
the required merge gate.

The first fresh Linux CI compilation also exposed an existing backdrop test's
unacknowledged use of experimental `Window.ThemeMode` (`WPF0001`). That deliberate
test call now opts in locally around the single assignment. No project-wide
diagnostics, backdrop assertions, MessageBox test bodies or deadlines are disabled;
the fresh CI source graph and actual dialog execution remain required. Running the
adjacent backdrop contract then exposed its missing Fluent resource dependency.
The test project now references the actual source `PresentationFramework.Fluent`
project; it does not substitute a packaged theme or skip the themed path. The
rebuilt source graph compiled with zero errors and five existing warnings, and
the expanded gate passed **8/8, zero skips** on macOS ARM64/.NET 10.0.5, including
the public MessageBox owner cases and the existing backdrop contract. CI runs
this same eight-test selection with the original 60-second deadline.

This is source ownership/input/lifetime evidence, **not native window-manager or
rendered-dialog qualification**. The test retains `ShowInTaskbar=false`, matching
current owned-dialog policy rather than claiming the issue's separate taskbar-icon
expectation. Windows continues to use its native user32 MessageBox route. X11
modal hints and Cocoa/native input suppression require their own actual platform
window evidence; this source gate does not close those outstanding contracts or
establish why the historical preview.45 sample behaved differently.
