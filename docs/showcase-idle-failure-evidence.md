# Passive Showcase failure evidence

This is diagnostic localization, not a resize fix or native idle qualification.
The original native presentation assertions, four exact-zero intervals,
30-second post-Loaded first-frame bound and 120-second child deadline remain
unchanged. The original gate has no debugger or renderer tracing switch. An
explicit failure-only diagnostic replay is described below; it cannot qualify
or replace the original result.

## Current-main integration: 525dba116

[Build 36331790928](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36331790928)
passed the x64 native Showcase job, Linux XWayland popup/input job and canonical
SDK consumer. The x64 idle intervals were `3→3`, `4→4`, `5→5`, `6→6`, with
source restoration complete. ARM64 exited `3221225477` (`0xC0000005`) after
`native-resized-boundary`; it did not time out. All 13 synchronous resize
checkpoints completed, including `FramebufferRenderDeferred`, setter return
and observed geometry. This localizes the new crash after the setter; it does
not identify a faulting module or prove a rendering/lifetime cause. No WER dump
or exactly correlated Application Error record was produced. This Build remains
failed and must not stage qualified packages or releases.

## Executed follow-up: 35d85a823

[Build 36336975867](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36336975867)
passed the prerequisite, SDK, consumer, AnyCPU and Linux popup/input jobs, but
both native Windows application jobs failed. It remains an unqualified producer.

The x64 application returned zero with restored UI and stable `2→2`, `3→3`,
`4→4`, `5→5` presentation counts. Its runner correctly rejected the scrolled
interval's actual `1998.105 ms`, below the unchanged `2000 ms` minimum. A single
`Task.Delay` completion did not prove that the independent monotonic observation
clock had reached its deadline. The observer now waits only the remainder of
that original deadline, with upward millisecond rounding and at most four timer
waits. It never reads presentation counters in that loop, restarts an interval,
waits for quiet, changes the two-second requirement, or lengthens the existing
application timeout. Nonadvancing/regressed clocks fail explicitly; the actual
two-endpoint elapsed result is checked before publication. Focused observer and
source controls pass 17/17, including an early timer wake and bounded bad clocks.

ARM64's original PID `3260` again exited `0xc0000005` after
`native-resized-boundary`, without a timeout or original WER dump. The separate
diagnostic PID `8772` reproduced the same boundary and exception, and retained
a validated 944,358-byte normal dump with SHA-256
`581423cc255b4050b5395dee67b9254f0479f421e3c205a5d9500ca49657c386`.
The faulting thread is `5040`; PC `0x7df4a63200f4` executes `ldrb w1, [x0]`
with `x0=0x2696e300000` in generated copy code. Its link register
`0x7fff32b406ec` maps to `d3d10warp.dll+0x306ec`. This directs investigation
toward the queued copy/resource lifetime; it does not yet establish a driver,
surface or renderer defect. Replay apphost cleanup also reported access denied
and remains explicit in its receipt. The replay does not qualify idle or replace
the original failure. Both downloaded artifact ZIPs match GitHub's recorded
SHA-256 hashes; logs, receipts, raw dump and LLDB observations are retained in
`artifacts/idle-current-integration/capture-ci.ofqFRCDQ/` of the isolated worktree.

## Separate failure-only native debugger replay

CI may explicitly supply the matching `ShowcaseNativeDebugger.exe`. The original
uninstrumented child runs first, with unchanged assertions and deadlines. Its
receipt and exit status are persisted before any replay. Only an access violation,
fail-fast or stack-buffer-overrun status without an original dump, timeout or
cleanup error permits one separate diagnostic invocation. A passing replay is
never a passing original run; every diagnostic receipt says `qualifiesIdle=false`.

The original C++ helper launches only a fresh, exact-byte `ShowcaseIdle-<id>`
apphost. It cannot attach to another PID. It uses the Windows SDK's native
`DEBUG_EVENT` and `CONTEXT`, verifies actual process architecture and rejects
emulation. The suspended child enters a private kill-on-close job before running.
Only the initial loader breakpoint is consumed; other first-chance exceptions
retain application handling, and second-chance exceptions remain unhandled after
capture. There are no register writes, desktop hooks or global crash-policy edits.

The helper writes `MiniDumpNormal` using the stopped faulting thread's context
and debugger-owned exception pointers. Foreign chained records reject explicitly.
The diagnostic child has a 110-second native bound inside the separate 120-second
runner bound; dump cancellation is requested after five seconds or 32 MiB. Raw
files remain outside uploaded evidence. Existing independent type/size/PID/hash
validation also requires the dump's thread, exception code and address to match
the debug event before publication. Cleanup retains the original failure.

Both Windows architectures compile and exercise the helper in early independent
CI jobs: ordinary nonzero exit, handled exception and unhandled access violation.
Their exact-head artifacts are diagnostic tools, never product package payloads.
Offline controls cover PE architecture, malformed receipts, dump correlation and
original-result preservation. These controls do not qualify ARM64 Showcase or
establish a crash fix. No new third-party executable/dependency is downloaded.

At `8b4c1945c`, ARM64 passed all three executed child controls; x64 passed launch,
handled exception and ordinary exit, but its dump failed the strict header gate.
That error did not retain the header fields, so the rejected flags/version are
not yet established. The follow-up error reports only bounded numeric header
fields, never raw memory, without relaxing normal-dump admission. The callback
now explicitly preserves default module/thread flags, handles cancellation and
declines alternate I/O, snapshot, kernel-dump and extra-memory requests; it must
not claim success for operations it does not implement or ignore read failures.
Native callback controls exercise these decisions before the real child controls.
Both architecture jobs must pass again; this is not evidence of a product fix.

The executed `b0957129a` x64 control then supplied the missing evidence:
signature `0x504d444d`, version `0xa0f4a793`, flags `0x00200000`, 13 streams,
12,521 bytes. Both architectures passed the callback contracts; ARM64 also
passed all child controls. The x64 writer still requested exactly `MiniDumpNormal`.
The system DbgHelp added `MiniDumpWithAvxXStateContext`, which the documented
[dump type contract](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ne-minidumpapiset-minidump_type)
defines as AVX crash-context registers, not additional process-memory collection.
The validator now admits only zero flags or that exact register flag, requiring
one complete, in-bounds `SystemInfoStream` with intrinsic x64 architecture for
the latter. It records the actual flags without rewriting the dump. Every other
flag bit, full-memory stream, invalid PID, truncation and size excess still
rejects. Synthetic controls exercise all 63 other bits and malformed/non-x64
system streams; real controls must independently pass on both architectures.

Implementation provenance is original LibreWPF process/receipt ownership and
ProGPU's existing installed-Visual-Studio discovery. Public contracts consulted:
[debug events and handle ownership](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-waitfordebugevent),
[MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump),
[local exception pointers](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_exception_information),
and [callback-specific return contracts](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/ne-minidumpapiset-minidump_callback_type).
No debugger/toolkit implementation source was copied.

## Exact-head follow-up: f2b46c2

[Build 36306597457](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36306597457)
completed unsuccessfully on both Windows architectures. ARM64 recorded an initial
interval of `3→4` presentations and unequal source/native state. x64 recorded
three stable exact-zero intervals (`2→2`, `3→3`, `4→4`), then timed out waiting
for the restored-window native Update endpoint. Both receipts report source UI
restoration. Neither failure supplies a native crash stack, and neither proves
the earlier ARM64 fail-fast resolved. No failed producer artifacts qualify releases.

The downloaded idle artifacts match GitHub's SHA-256 digests: ARM64 artifact
`10929085218`, `b4fe70d5dddc12e57a9b5e5d53ccd050e1f3f6d2a6c71b1465250a433b1acba7`;
x64 artifact `10928820768`,
`0f1c5cd00c57a2e4df7d952be2a32a8c757b3206b12f73e9b455362d88dbc41b`.

The receipt now retains both actual endpoint values before comparing them,
including presented scene/WPF/drawing revisions, viewport, source placement,
clips and native counters. `after: null` means the endpoint was not obtained,
not unchanged state. Full text remains part of equality; only its length and an
explicit text-change flag are serialized. No extra observation, native query,
interval logging, render request, settling time or deadline is introduced.
This closes the missing comparison evidence, not either application failure.

## Retained ARM64 failure and x64 control

At `ba7dcd7cd7288d9e0f2d2a3c07e44514489e50d2`,
[Build 36288813259](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36288813259)
retained an ARM64 child exit `3221226505` (`0xC0000409`), not a timeout. Its
journal reached `initial-observed`, `scrolled-observed`, then
`native-resize-request`; its final application receipt was empty. Before/after
payload hashes matched and no WER dump was produced. The original request
marker preceded dispatcher enqueue, so it did not establish that the resize
callback or native setter had executed. This status alone supplies neither a
faulting module nor a stack or fast-fail parameter; it is not proof of a
particular memory, rendering or lifetime defect.

The same-head x64 receipt succeeded: initial `2→2`, scrolled `3→3`, resized
`5→5`, restored `7→7`, with source restoration complete. This is an independent
x64 control, not ARM64 qualification. Both original artifacts/logs remain at
`/Volumes/1TB-macOS/librewpf-183-ci-triage.8wTXEi73/{arm64,x64}`.

## Additional checkpoints

The idle resize callback now records these transitions in the existing fresh,
flushed phase journal:

1. `native-resize-callback-entered`, immediately before the original setter.
2. `native-resize-setter-returned`, after `host.SetClientSize` returns.
3. `native-resize-wake-returned`, after the original explicit wake returns.
4. `native-resize-geometry-observed`, after the existing geometry wait completes.

The first three expand the existing two-action size helper without changing
their order or adding render requests. The fourth is before the next settling
and passive observation. None writes inside either endpoint-to-endpoint
measurement. A last marker localizes progress only; callback return is not GPU
completion or proof that deferred work succeeded. There remain at most 17
markers in the successful sequence, below the existing 32-record journal cap.

## Failure-only Windows Application Error records

Only the already opted-in CI Windows crash-capture child can trigger
`eng/showcase_idle_events.py`. The existing unique exact-byte apphost, per-image
WER ownership and dump collection stay unchanged. After a non-timeout child
failure, a separate read-only helper queries the local Application channel
using the documented [EvtQuery](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtquery),
[EvtNext](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtnext)
and [EvtRender](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtrender)
APIs. No subscriptions, log clearing, remote sessions or registry writes occur.

Admission requires provider `Application Error`, event 1000, the exact unique
apphost name and full Windows image path, its **faulting EventData ProcessId**,
and a UTC event time between the runner's pre-launch timestamp and this one
post-failure query boundary. The event logger's System/Execution PID is never
treated as the crashed process. The unique image also prevents PID reuse from
matching a different invocation. UTC boundaries are retained; a wall-clock
regression is unavailable evidence, not an expanded search window.
Comparison preserves the event's seventh fractional digit (100ns); an event
just beyond either boundary is not admitted by microsecond truncation.

Only the correlated fault module/path, exception code, offset, process creation
time/report ID and event identity are retained. Unmatched raw XML, machine names
and other process records are never written. WER event 1001 is deliberately not
collected: filename-only fields without the owned faulting PID do not satisfy
this correlation contract.

The helper has a five-second hard process bound, no retries, a 100ms `EvtNext`
wait, at most 16 candidate records, 64KiB per XML render, 256KiB aggregate XML and
a 64KiB fresh JSON receipt. Reaching the record cap is explicit. All event/query
handles are closed on the same thread; timeout kills only this diagnostic
helper. Missing, inaccessible, late, malformed or unsupported event records
remain explicit unavailable diagnostics. The helper cannot change the original
child exit, timeout, cleanup status, or success judgment. This extra
post-failure budget never lengthens the application's 120-second bound.

## Owned debugger child retirement

Build `36336975867` captured the ARM64 failure in its separate native replay,
but that replay reported `WinError 5` while deleting its uniquely owned apphost.
The debugger had accepted `EXIT_PROCESS_DEBUG_EVENT` as a completed process
without observing termination after continuing the event. The helper now waits
on its original `CreateProcessW` process handle after a successful exit-event
continuation, using only the remainder of the existing 110-second deadline.
It reports `exited: true` only after the process is signaled and its actual
exit code matches the debug event. Timeout/API errors remain explicit failures;
there is no new retry, extended timeout, or unrelated-process cleanup.

This follows the distinct Windows contracts for
[continuing debug events](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-continuedebugevent)
and [process termination](https://learn.microsoft.com/en-us/windows/win32/procthread/terminating-a-process).
Native x64/ARM64 controls delete their copied fixture immediately after the
helper returns, before reading the dump, without sleeps or retries. The offline
source control checks ordering, the original deadline and fail-closed receipt.
These checks do not prove the original ARM64 Showcase crash resolved or establish
its apphost cleanup on an executed application replay; those remain independent
CI application requirements.

## Local validation boundary

Offline checks: 16 event/XML/ABI/order controls, 20 launcher/child controls and
15 original crash-policy/minidump controls pass. The existing C# source guard is
extended without changing its case count; its full compilation/execution remains
the normal CI gate. Workflow actionlint and `git diff --check` pass. No Windows
event query, Showcase launch, GUI, VM, native render, debugger or full build was
executed for this change. The next actual failed run may still produce no event
or dump; these diagnostics do not resolve or reclassify the original crash.
Local logs are retained under `artifacts/idle-crash-evidence.GEXTqK/` in the
isolated `librewpf-idle-crash-evidence.9G281lnq` worktree.
