# Native pointer host transport

The Silk input service selects `NativeWindowInput` by actual provider. When the
context supplies native pointer events it subscribes to that stream alone, not
the equivalent Silk mouse callbacks. Keyboard delivery remains separate. Native
double coordinates, timestamp, button/click identity, modifier snapshot, scroll
units and phases cross the host as immutable `PortablePointerInput` data.
The actual provider's scroll-protocol tag travels with them, including through
coordinate copies. Neither the host OS nor recognizable raw phase bits select
AppKit semantics for an untagged provider.
Command-to-Control shortcut normalization is separate from the original flags;
pointer delivery never polls a later keyboard state to replace its snapshot.

Coordinate conversion preserves the complete packet. Precise scroll vectors use
the same client-frame scale as pointer positions, without desktop-origin offsets;
wheel-line deltas are never scaled or multiplied by 120. Owned view-local input
does not use the old Cocoa GLFW owner-coordinate heuristic. Native popup routing
does not discard captured drags/up or leave events outside the popup rectangle.
Hide cancellation still reaches the popup's source after visibility is cleared.
Owner-window cancellation is not redirected into an arbitrary overlay popup.

Subscription disposal keeps native cancellation subscribed while the provider
retires. Reentrant disposal is guarded. A throwing source handler cannot prevent
remaining unsubscribe/context-map cleanup; failures still propagate. Actual
source teardown retains the separate source-owned button/capture rules.

The source registrar must explicitly implement `IPortableNativePointerInputService`.
Absent or rejecting capabilities fail; native fields are never discarded into a
legacy wheel event. Legacy constructors, input services and source paths remain
available unchanged. Cancellation/leave without their native packet also fails
instead of reaching a provider that cannot interpret those new event kinds.

The earlier focused host run passed 401 cases: 249 window-host cases, 44 Silk input cases,
15 native transport cases and 93 activation cases. It includes complete popup
routes with typed recording source/native hosts, not real AppKit windows. The
existing fast CI gate now includes these classes and per-class minimum counts.
Activation and popup fixtures share the same test collection because both replace
the process-wide source registry; this does not serialize unrelated test classes.

The canonical LibreWinForms dependency pins the same ProGPU commit as this host.
The existing source-integration identity check remains unchanged; upgrading only
the outer ProGPU pin fails that check. Package staging still requires the entire
producer Build to succeed on the exact shared commit.

That shared revision also repairs System.Drawing's captured clip mapping when a
float determinant or inverse overflows but the relative mapping is representable.
The ordinary float path and rejection of genuinely unrepresentable mappings stay
unchanged. This is a drawing dependency repair, not a change to native pointer or
scroll coordinate policy, and it does not opt the source registrar into native input.

The canonical Forms dependency also guards source pointer continuations across
public hover/focus callbacks. Nested input owns its new hover and capture; a
disposed, hidden, disabled, reparented or recreated recipient cannot receive the
old event. Reopening the same Forms popup object starts a fresh handle-bound hover
lifetime. The fix and its source regressions belong to LibreWinForms, not a WPF
host copy, and do not enable either framework's owned native input factory.

The same canonical dependency transfers physical hover across Forms top-level
windows independently of keyboard focus. Moving between an owner, dropdown and
submenu retires the previous hover before callbacks, reusing canonical item-leave
and timer cancellation. Twelve additional source regressions cover that boundary;
they do not qualify native OS leave, cross-window capture or WPF pointer routing.

Canonical Forms also handles its backend's input-loss notification independently
of whether a nonactivating popup ever held keyboard focus. Capture, pressed state
and hover retire before public callbacks; per-button window ownership protects
another window's input. New pointer/focus generations survive old cleanup, while
throwing callbacks cannot roll back a still-current focus loss. This remains the
existing Forms input contract, with fifteen authored source regression cases;
it does not opt either source registrar into the native pointer provider.

The aligned dependencies also carry Forms' explicit pointer Leave/Cancel
boundaries. Leave retires exact-window hover while preserving capture and held
buttons; Cancel retires only that window's capture, presses and hover without
keyboard focus loss or synthetic up/click delivery. Nested input and another
window's held-button ownership remain authoritative.

ProGPU's native popup owner can now be assigned after hidden panel creation,
without replacing the rendering view. Its provider-aware retirement API reports
pending callback/view ownership. Forms' actual dispatcher retains the native
window and failed renderer-cleanup owner until both retire, hides separately,
and never destroys the native surface after renderer cleanup fails. Retries run
after existing polling/callbacks; dispatcher shutdown cannot abandon a pending
owner. Both superproject pins select the same immutable ProGPU revision. These
dependency changes do not select either framework's unfinished native factory,
change scroll compatibility or establish native modal/UI qualification.

The dependency graph also includes the shared typed popup preparation/display
API and its actual Forms consumer. Forms passes its `IWindow`, preserving hidden
owner binding, renderer-before-show ordering and provider-owned callback lifetime.
An opaque Cocoa panel is never treated as a GLFW window. Rejected setup retains
the existing source teardown/dispatcher retirement path rather than falling back
to an unowned or activating window.

Canonical Forms input transparency now uses the same provider-aware boundary. An owned
panel retains transparency independently of enabled state across owner changes,
input-context replacement and reopening; policy changes preserve cancellation
generations and discard stale pointer tails. GLFW pass-through uses the actual
`Native.Glfw` identity and checked attribute readback. Both outer dependency pins
advance together to keep the source-first package graph identical to Forms' own
ProGPU dependency. These changes do not enable a source factory or resolve the
remaining native-scroll compatibility and desktop qualification requirements.
WPF's decoration service now passes the actual popup `IWindow` to the same shared
prepare/show APIs. Each show revalidates its current native owner on Windows, X11
and Cocoa before invoking the source callback. Owned panels retain their dispatch
lifetime around that callback; ordinary providers retain their native owner checks.
Closing intent alone does not replace validation of the still-live owner's native
identity, because an application may cancel closing from its confirmation dialog.

Nonactivating visibility also uses the shared provider operation. GLFW restores
its actual prior focus-on-show setting on the same surviving native identity;
owned Cocoa visibility updates the retained window state. A provider/native failure
propagates instead of requesting the host's ordinary activating fallback. The old
raw Cocoa ordering and unconditional GLFW flag-reset helpers are removed. Twelve
authored adapter cases join the existing host-class fast gate (minimum 268), and
the source-graph checks require these shared calls. Full package and real desktop
qualification remain independent; source factory/scroll admission is unchanged.

## Remaining source and application work

The canonical Forms dependency now binds each drag operation to its own input
registration. Old queued input cannot enter a later drag, registration/teardown
failures release the service state, and teardown preserves the original source
error. Drag completion is committed before application Drop or cancellation Leave
callbacks, so reentrant release/Escape cannot finish that operation again. This
does not qualify reentrancy in nonterminal drag callbacks. The update changes the
actual canonical dependency graph without changing its
shared ProGPU identity. Native drag cancellation, source factory selection and
desktop qualification remain separate; only a whole successful exact producer
Build may supply release packages.

Host drag-layout tracking retains an identity for each button press. Mouse-up
cleanup retires only the press observed before its source callback; a nested
same-button press survives cancellation, deactivate/reactivate or hide/show,
including a throwing callback. A different button cannot keep the old press alive,
and an unmatched release cannot clear a newly started press. Sixteen direct and
queued-input regression cases join the activation fast gate (minimum 109).
This preserves the per-drag layout boundary without changing source capture or
raising synthetic input. Real gallery slider and popup qualification is separate.

### Canonical Forms package consumption

LibreWPF now pins qualified LibreWinForms
`05b199f5f60af462d5a89f6909432273126a4ecf`, including
[native click delivery](https://github.com/wieslawsoltes/LibreWinForms/pull/136)
and [TextBox pointer default ordering](https://github.com/wieslawsoltes/LibreWinForms/pull/137).
The canonical package builder and mixed-desktop SDK consumer derive their Forms
version from this gitlink; no parallel WPF-local editor implementation or manual
package-version override is introduced. Both repositories keep the same ProGPU
`bb66c0f83622a68fd41a8f81f2a78134139a4f59` identity.

The exact Forms producer
[Build 36673923226](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/36673923226)
passed all nine jobs, including 885 canonical cases, 16 focused pointer-order
cases (zero failures/skips), and installed-package checks on Windows, macOS and
Linux. LibreWPF's unchanged canonical integration, package-mode SDK and platform
CI gates must independently qualify the updated consumer graph. No local builds,
tests or native windows were run for this dependency update.

Native counts retain source-local click pairing and canonical notification order.
Plain TextBox defaults precede virtual/public mouse handlers; handler selection
and capture changes survive, while stale layout/input continuations stop. These
are source fixes, not double-click word-selection, legacy-wheel compatibility,
native factory admission or desktop popup parity.

### Qualified Forms native-session host integration

The canonical Forms gitlink advances from
`05273a1c63a1e9f8c152e8e353ebe86775859133` to the exact
[Forms #148](https://github.com/wieslawsoltes/LibreWinForms/pull/148) producer
`4badfec8dea1466a908f05e828c5461c422e136a`.
[Build 37032422405](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/37032422405)
completed successfully on that exact head, with all nine jobs passing, including
canonical source, AppKit, package and Windows/macOS/Linux visible-package lanes.
The commit is the second parent of merged Forms default
`ea03ee63997f094429aee99d1692ece4e929949e`; the consumer pins the tested producer,
not a later branch tip. Its source host retains native-session release proof
across polling, Hide/Close/retirement and reentrant Show, including an absent
native query before an outstanding completion arrives.

This integration is stacked on WPF #234 at
`520d5e127363ae7922ffa4064471d0d893722d4e` without altering that host implementation.
WPF and the new Forms producer both retain ProGPU
`48a49afeb993214c0c40c6908e9896ef0b5dec97`. Canonical source-graph identity,
exact-successful-Build native staging, package/API and runtime admission checks
are unchanged. Automatic modality remains disabled; this source pin does not
qualify all-platform popup/UI behavior. No runtime is staged and no local full
graph/native/VM execution accompanies the update. The eventual complete WPF
dependency union still requires its own exact-head whole Build and required
checks; its expensive validation is deferred until that union is ready.

### Remaining native factory admission

Host disposal now retains the exact native provider while
`NativeWindowLifetime.TryDispose` returns false or throws. The existing global
post-dispatch drain keeps pending entries instead of clearing them before calls;
it attempts only windows created on the current thread, completes other eligible
hosts after a failure, and rethrows the first failure. Source leases retire before
renderer resources; active render/native/modal callbacks defer renderer and native
destruction. Failed renderer cleanup retains its target for retry. Event detach,
source cleanup and provider disposal are guarded against nested retirement, and
pre-event render retirement cannot continue reading the retired provider.

Eighteen authored source-lifetime cases cover deferred and failing providers,
resource-before-native ordering, creating-thread rejection, windowless source and
resource cleanup reentry/retry, native callback deferral, unsubscription reentry,
queue peer continuation, preservation of the original exception, the pre-event
render tail and source disposal from the actual Closing callback. The host-class
minimum increases from 268 to 286. These use typed
provider identities/headless source fixtures, not actual NSPanel or GPU execution.
No local compilation, tests or native execution were performed for this change;
the complete exact-head CI and native application gates remain required.

Ordinary accepted close during an active render now captures the exact target,
stops subsequent source drawing/presentation work at callback boundaries, and
retires that target from `OnRender`'s outer `finally`. Drawing contexts/provider
registrations and the managed/native presenters' texture-view/texture release
scopes therefore unwind before target cleanup. Cancel and hide/show alone retain
the target; nested show or a later canceled close cannot revive an already
accepted old frame. Non-rendering accepted close still cleans up synchronously,
and ordinary close does not acquire ownership of native-window destruction.

Failed cleanup remains in the existing creating-thread queue. Even if target
disposal succeeded before scheduler reset failed, new load/render admission is
blocked until that exact cleanup completes. A replacement target is never disposed
on behalf of the old frame. An original frame exception takes precedence over a
simultaneous cleanup exception; no arbitrary exception-data callbacks are invoked.
Nine further authored cases exercise the actual headless `OnRender` dispatcher /
`Close` / `OnClosing` path, scope unwind ordering, cancel/hide-show, accepted-target
identity, reset failure/reentry, host-disposal transfer and replacement rejection.
Source guards retain managed/native texture release scopes. The host-class minimum
is now 295 (286 + 9). No local compilation or execution was performed; these are
not acquired native GPU frame, popup/UI or installed-package qualification.

The preexisting outer `RunPortableNativeLoop` close-associated
`ObjectDisposedException`/`InvalidOperationException` catch policy is unchanged.
That policy can suppress such a callback exception once close has started; this
source observation is separate from the new frame/cleanup exception preservation
and is not evidence of an observed application failure.

The actual source registrar does not advertise native-pointer capability yet.
The host now has an owned Cocoa factory connection gated by a bound portable
source, that explicit registrar capability, and the shared owner's actual Cocoa
identity. Ordinary windows and non-Cocoa owners retain their existing factory.
Once the owned path is admitted, a missing owner or creation failure propagates;
it cannot silently create an ordinary NSWindow. The same shared-device and
surface-before-window teardown paths remain in use. Seven authored factory cases
remain in the expanded host-class CI gate; no native panel or UI qualification is
claimed. Because the real registrar has not opted in, this connection does not
yet select the owned factory in applications.
The internal [source report path](native-pointer-source-reports.md) now retains
native positions, time, click identity and source generations for movement and
five-button input. Source hide/modal cancellation now retires owned presses and
exact-provider capture before callbacks. Source hover/leave retirement retains
native metadata and original physical-source ownership without cancelling capture.
The [source scroll consumer](native-scroll-source-consumer.md) preserves point/line
units through real source metrics and the command queue. Its internal
[routed path](native-scroll-routing.md) now validates declared AppKit phases and
retains momentum targets, generations and fractional state. Independent nested
axes now route through source-frame remainders, and existing cross-source routes
use explicit desktop/root transforms. Custom providers can declare their point
units publicly; ordinary IScrollInfo still supports native line commands. Legacy-only
handlers, actual popup-route qualification and source
admission remain unfinished.
The owned Cocoa factory is therefore still not selected. Complete
source consumption, callback/queued-dispatch lifetime, Forms integration and real
native popup interaction/visual tests remain required. No automatic modality,
package/UI parity or native capture qualification is claimed by this host bridge.

## Actual source registrar connection

The later source integration now implements `IPortableNativePointerInputService`
on the real PresentationFramework registrar. Both entrypoints use the existing
native report/scroll implementation. Window delivery requires that exact live
Window to remain the presentation source root; source delivery accepts only an
actual portable source and retains its thread, lifetime and generation checks.
Callback root replacement cannot deliver the remaining old down to the new root.
Cancel/Leave retain their source-cleanup path rather than ordinary input filtering.

This makes the existing source-capability check reach the owned Cocoa factory.
WPF already knows its actual owner and shares that owner's render device before
hidden initialization. It therefore keeps `CreateOwnedCocoaWindow`, including its
fixed managed parent and owner-loop wake callback; it does not migrate to the
ownerless Forms creation lifecycle. Existing hidden NoAPI/context-control options,
typed native input/cursor provider, checked owner preparation/show and renderer-
before-window retirement remain authoritative for both WPF renderer modes.

The owner alone polls global modal events. An externally pumped popup drains its
own window queue after the owner UpdateTick instead of invoking another
`NativeWindowModalSession.TryPumpEvents`. Initialization, show and input failure
cleanup preserves the primary error; failed native/render cleanup remains owned
by the source host's creating-thread retirement queue.

Popup input-context attachment failures now propagate instead of being swallowed
by the ordinary host's optional-input compatibility path. The popup adapter also
keeps native `Handled` independent of accepted delivery; its legacy callback
convention cannot turn an unclaimed native scroll into successful consumption.
Three adapter cases retain unhandled/handled native results and unchanged legacy
behavior, raising the host minimum from 301 to 304.

Four new actual-registrar source cases cover unchanged point/line packets with
unhandled results, foreign/detached/retired targets, callback root replacement and
native cancellation metadata/capture retirement. Existing routed-scroll cases and
the shared native-pointer helper now traverse the actual registrar. Six host cases
cover modal-poll ownership and real popup-adapter initialization failure with
retirement retry. The source/host gate minima increase to 32/301 without changing
deadlines or skip rejection. These cases are authored, not yet execution evidence.

Native scroll deliberately stays on `PortableScroll` and measured source
`IScrollInfo` contracts. Delivery acceptance does not mean motion was consumed:
unclaimed point/line events remain unhandled and are never converted to fabricated
legacy wheel notches. Ordinary Silk `MouseWheel` remains unchanged. Legacy-only
custom handlers still require explicit compatibility work. Automatic native modal
sessions, real AppKit input/capture/rendering, both source application paths and
full package/desktop qualification remain separate gates.

The later integration pins exact Forms popup source
`4f173ac601d96fbac2a82fc1e1f9b8726c885c0c` and ProGPU owned-option source
`f22b5b3f3416e16421ab6b2952b2eef1864d781e`; that Forms commit pins the same
ProGPU commit. This is a coherent pending source graph, not qualified package
provenance. The Forms post-commit helper run passed 132 cases with no skips;
WPF source/host compilation and tests and complete producer/consumer Builds
remain separate evidence. No pending native runtime is built, staged or admitted.
