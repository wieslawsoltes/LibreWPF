# Native-session close ordering

The bounded application path is closing or disposing a Showcase dialog/popup
host while an explicitly owned native modal session still borrows that window.
Automatic session activation remains disabled. At default `ca84e141a`, host Hide
and native retirement already checked the shared session, but both explicit
Close and Dispose's deferred-close request called `IWindow.Close` immediately.
The pinned ProGPU `48a49afe` owned Cocoa provider hides before raising Closing,
so deferring only disposal or reacting in Closing cannot protect this boundary.

The host now retains the exact pending-close `IWindow` before requesting shared
release. Both close entry paths use that prerequisite. Repeated requests coalesce;
Show does not cancel a Close. Completion rechecks window identity and all native
leases, including a session begun since the earlier release. Synchronous release
is supported without duplicate provider calls. Actual provider Closing still
owns cancellation and accepted-close renderer retirement.

Dispose supersedes a session-deferred Close: completion uses the existing
creating-thread retirement queue instead of issuing a second Close/Hide. The
source-owned loop already stops on disposed state. Pending release, rendering,
native dispatch and loop ownership continue to prevent resource/native release.
Failed release without a delivered callback latches the original exception and
retains the pending window, even when failed native identity disposal has removed
the shared session query. Later Close and retirement cannot interpret that absence
as release or retry uncertain native cleanup. Callback-delivered source Closing
or retirement failures remain distinct and retain their original exception without
replaying Close. Failed retirement keeps its queued cleanup owner. The shared
session owns nested LIFO release and permanently uncertain native-End failure.
No source callback can claim release by observing only that session Dispose returned.

Hide, Close and source dialog completion now share one host-wide exact-window
release prerequisite and uncertainty latch. Actions coalesce while the original
native callback is outstanding; a later empty native query cannot bypass it.
Ready callbacks run in request order and each rechecks new leases and source
intent. A callback's original error propagates after other ready callbacks run.
Outstanding actions keep retirement owned; the last completion retries disposal
on the creating thread even when this host no longer runs its ordinary loop.

An accepted provider Close owns its hidden view. Hide callbacks that follow it
do not write visibility, since owned Cocoa rejects setters once closing. Ordinary
Hide reads actual visibility and rechecks intent and native retention afterward,
so a reentrant getter cannot hide through a new lease or superseding Show/Close/
Dispose. Show may still supersede ordinary pending Hide, but cannot publish native
visibility after a failed or undelivered release with no current native lease.
Ordinary nonretained Close, typed native identity, owner-only polling and popup
local queue drain remain. No automatic Begin, input policy, package/gitlink update
or native implementation is added.

## Focused coverage and limits

Thirty-seven actual-host source controls cover deferred and synchronous release,
coalesced/reentrant Close, Show while Close is pending, fresh leases before Close
and Dispose, native cancellation, Dispose supersession with no loop/active render/
active loop, Dispose-initiated close, original release/Closing/retirement errors,
wrong thread and replacement-window rejection. Undelivered failure is checked
both with retained identity and after the identity disappears; callback-delivered
Closing failure is checked synchronously and asynchronously. They use recorded provider
operations and a per-host session-release seam; no native panel, renderer or GPU
is fabricated or executed. Existing host/render-close tests remain selected.
Twenty additional controls cover both Close/Hide callback orders, normal
Hide/Show supersession, new leases and changed intent from the visibility getter,
uncertain Hide/dialog failures with retained/disappeared identity, synchronous/
asynchronous delivered callback errors, fresh dialog leases, ordered callback
failure continuation and an undelivered callback despite an absent native query.
Both the visibility getter and retention query may queue dialog release and then
observe absent retention without delivery. Hide coalesces with that genuinely
pending callback before writing; already-ready callback batches are distinct.
The hosted source gate minimum increases from 321 to 358 without changing
selectors, deadlines or failure requirements.

At implementation checkpoint, compilation and execution are pending the exact
hosted source graph. No verified current local managed host cache was available;
older mixed assemblies are not used as product evidence. Postcommit bounded
syntax and source checks are recorded separately, not desktop or modality parity.

Postcommit checks at `79f9a108aec44ab7a146d82ad153d0d3f22399e8`
passed: SDK Roslyn parsed both changed C# files with zero syntax errors;
`git diff HEAD^ --check`, `bash -n eng/progpu-wpf-layout-clip.sh`, and Python
compilation of its unchanged embedded receipt verifier passed. The focused
inventory is eight Facts plus seven InlineData cases; the existing host selector
and new minimum 336 include all fifteen. Source wiring checks retain one provider
Close site behind shared release, both caller paths, the pending-window retirement
prerequisite, and unchanged owner/local-queue polling. These checks do not compile
types or execute host/native tests.

The uncertainty correction at `0be22e60b7c863d149af967887f9172a1279cc26`
passed the same postcommit checks. The final inventory is six Facts plus eleven
InlineData cases (17), selected by the unchanged host filter at minimum 338.
Additional source checks require the original-error latch at Close, deferred
completion and retirement, and distinguish an undelivered failure from a
synchronous callback exception. Type compilation and execution remain hosted-only.

The original `5c4f8ac76` hosted Build `37027771499`, source job `110906674521`,
compiled and executed 1,060 selected cases: 1,057 passed, three failed, zero
skipped. Two new lifecycle controls reached legitimate scheduler `DoRender`
wakeups that their strict provider did not yet model; the third retained an old
exact source guard for the pre-pending-close coalescing condition. The followup
adds tracked provider rendering through the fixture's real headless `OnRender`
callback (no GPU), asserts those wakeups, and updates the exact coalescing/identity
guard. No product behavior or native assertion is changed to satisfy those tests.
All 37 lifecycle controls still require execution on the followup hosted graph.

Postcommit checks at `e11e8b2e1cfd2a9e0c289cf8102904a4aec360d7` passed:
SDK Roslyn parsed all five changed C# files with zero syntax errors; shell syntax,
embedded verifier syntax and `git diff 5c4f8ac76 --check` passed. Exact inventory
is the original 17 plus 18 new cases, selected by the unchanged host filter at
minimum 356. Source checks confirm the single shared release boundary, coalesced
outstanding callbacks, undelivered-error latch, creating-thread retirement drain,
visibility-read/new-lease/write ordering and unchanged owner/local polling.
These are source/syntax checks only, not type compilation or test execution.

The getter-proof correction at `56df0685a68dbada8e7c472addbc01061e808240`
passed the same bounded checks. Final inventory is 17 original plus 20 additional
cases (37), with unchanged host selection and minimum 358. The source-order check
now requires visibility read, then the retention query plus outstanding-callback
proof, then the guarded visibility write. Hosted execution remains pending.

Final review retains the strict one-outstanding-provider-callback fixture: only
the host coordinator may coalesce source actions. A delivered retirement failure
does not trigger another cleanup attempt at the end of that same callback batch;
the exact attempt-count control requires the second attempt only at the explicit
later creating-thread drain. Other ready callbacks still run before the original
failure propagates.

## Delivered identity rejection and explicit retry

Exact `919f548cf` Build `37030777728`, source job `110916754297`, compiled
and executed 1,080 cases: 1,079 passed, one failed, zero skipped. The unchanged
replacement-window Close control exposed a delivered-callback bookkeeping bug:
the coordinator rejected the replacement before clearing the Close action's wait,
so restoring the original window could never retry that retained Close.

Each ready action now acknowledges delivery separately on the creating thread,
before the exact-window action guard. Close retains its original pending window;
Hide similarly clears only its delivered wait. Replacement-window actions still
throw before any provider access, without latching a delivered source error as
uncertain native release. Undelivered failures remain blocked. The original Close
expectation of exactly one successful retry is unchanged and now checks zero
replacement accesses, original identity, empty proof/count state and two release
attempts. A paired Hide control raises the lifecycle inventory to 38 and hosted
minimum to 359; selectors, deadline, pins and automatic-modality gates are unchanged.
