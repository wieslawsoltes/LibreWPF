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
Failed release retains the pending window; failed retirement retains the queued
cleanup owner and original exception. The shared session itself owns nested LIFO
release and permanently uncertain native-End failure. No source callback can
claim release by observing only that session Dispose returned.

Existing latest-intent Hide, ordinary nonretained Close, typed native identity,
owner-only event polling and owned-popup queue drain are unchanged. No automatic
Begin, input policy, package/gitlink update, or native implementation is added.

## Focused coverage and limits

Fifteen actual-host source controls cover deferred and synchronous release,
coalesced/reentrant Close, Show while Close is pending, fresh leases before Close
and Dispose, native cancellation, Dispose supersession with no loop/active render/
active loop, Dispose-initiated close, original release/Closing/retirement errors,
wrong thread and replacement-window rejection. They use recorded provider
operations and a per-host session-release seam; no native panel, renderer or GPU
is fabricated or executed. Existing host/render-close tests remain selected.
The hosted source gate minimum increases from 321 to 336 without changing
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
