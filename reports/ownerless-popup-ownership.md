# Unattached portable Popup ownership

## Application path and defect

[LibreWPF issue #148](https://github.com/wieslawsoltes/LibreWPF/issues/148)
reports an editor hover popup constructed without a `PlacementTarget` or visual
parent. The source `Popup.GetTarget()` legitimately returns null. Before this
change, `TryCreatePortablePopupSource` rejected that request before calling any
registered host, and `BuildWindow` raised `PlatformNotSupportedException`.

The acceptance path is the existing Showcase application's live popup gate:
open, close and reopen an unattached popup while the real host is active. It uses
the same source ownership path in native MIL and managed portable rendering.
This is a source-backed defect and regression scenario, not a claim that the
reporter's complete SharpDevelop application has been qualified.

## Ownership policy

Only a null source placement target uses the existing
`AccessKeyManager.GetActivePresentationSource` selector. It already requires a
live, same-dispatcher portable source and a typed active scope; real Window
roots additionally enforce visibility, activation and modal-input admission.
Missing or ambiguous active sources remain rejected. Retained keyboard focus,
registration order and application MainWindow are not substitutes for activation.

A nonnull placement target remains authoritative, including a detached target
that cannot supply a source. The selected source is retained as the popup's
actual input owner until destruction; later activation changes do not migrate an
open popup. After destruction, a reopened popup resolves its owner again; a
rapid close/reopen before asynchronous destruction retains its live source.

The request still carries a null `PlacementTarget`. Source placement and screen
offsets remain unchanged; the active Window is not installed as a fake target.
Existing desktop/device conversion, native versus owner-surface selection,
host rejection cleanup and native Windows-MIL routing remain intact.

## Validation boundaries

Source ownership tests cover the public `Popup.IsOpen` path, default and absolute
placement, selected owner identity, transport scale, activation changes across
reopen, explicit target precedence, other dispatchers, ambiguous/ineligible
sources and rejected or invalid hosts.

The live Showcase gate requests actual native activation and requires source
`IsActive`, then uses its existing presented-popup and GPU input-index checks
for both open cycles, one owned source while open, and zero owned sources after
each close before reopening. Screen points use `PlacementRectangle` so client
offset scaling cannot scale desktop coordinates twice. It does
not fabricate activation, create another acceptance application, reduce existing
popup assertions or increase validation deadlines.

Compilation, focused source tests, complete PR CI and actual platform/package
execution are separate evidence. A test host's accepted source is not native
window, pixel, modal-session or full application qualification. Runtime results
will be recorded on the PR after execution.

Local macOS ARM64 evidence: the source Release build succeeds and all 29 focused
`PortablePopupOwnershipTests` pass with portable media explicitly selected,
including both public `IsOpen` cases. The rebuilt bridge's 110 project-graph
tests pass. Four neighboring activation tests cannot complete because their
existing test output omits `UIAutomationProvider` or `PresentationFramework.Fluent`;
this is recorded separately from the passing popup suite. The expanded Showcase
package/live gate remains required on the exact PR head.
