# Checked Cocoa popup Show integration

## Core action and change

Acceptance is opening the Showcase's menus/ComboBoxes and the external SDK
application's actual popup checks, using native MIL and the native input index.
The previous hidden-attachment requirement rejected AppKit's visible-on-add
behavior. The resumed core-fix request follows the proposed prepare/show contract.

ProGPU now owns hidden admission and a checked native Show boundary. WPF initializes
the hidden surface, prepares its owner, reapplies source desktop position, publishes
native modal input admission, then invokes the shared owned Show operation. Cocoa
attaches the actual child before the host nonactivating Show callback and verifies
identity, parent, visibility and flags afterward. Hide detaches natively; reopen
reattaches. Failure disposes the popup, without an unowned or composited fallback.
Win32/X11 preserve their existing hidden configuration path. This is not AppKit
modal popup admission, native nonclient suppression, or broad Direct2D/Win2D parity.

## Executed evidence

- ProGPU Cocoa ownership policy: 14 passed, zero failed (27 ms), including hidden
  preparation, visible-on-add, repeat Show, reopen and failed-callback cleanup.
- WPF host source: build succeeds, one existing unused-event warning, zero errors.
- Native external SDK RUN: exit 0 and
  `External SDK Application.Run validation succeeded.`
- Native external SDK LIVE: exit 0 and
  `External SDK apphost live input validation succeeded: logical 320x200, pixels
  640x400, viewport 640x400@0,0, dpi 2; mouse title click, TextBox text input, and
  Ctrl+E KeyBinding updated.`

Logs are `external-popup-show-run.log` and `external-popup-show-live.log` under
`artifacts/native-exact-b54db165.dBPf7B`; build/test logs are
`/tmp/wpf-popup-host-build.log` and `/tmp/progpu-popup-tests.log`. The application
uses staged current host/backend assemblies and the built native library over
the earlier dependency-snapshot packages. This is diagnostic execution evidence,
not exact final-head package qualification or full Showcase completion.

## Remaining merge blockers observed this turn

LibreWinForms PR29 is green. ProGPU PR139 has two actual Windows package-consumer
failures: x64 reports missing independent rectangle ink in the native cubic
fixture; ARM64 passes that fixture but later crashes with 0xC0000005 in native
BeginHitTest. See run 34566152842, jobs 103353012827 and 103353012738. LibreWPF
PR115's SDK package smoke was cancelled, not passed. Repair/reproduce those
failures, synchronize dependency pins and qualify exact final packages/platforms
before ordered merges. No CI assertion, pixel threshold or input gate is waived.
