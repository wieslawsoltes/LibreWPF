# Passive Showcase failure evidence

## Original GlyphRun crash and first-chance capture

[Build 38314985874](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38314985874)
at `f9507dc67` passed fifteen jobs and failed the ARM64 native Showcase job.
The original child exited `0xC0000005` after 63 seconds, following all four
successful zero-presentation intervals and source restoration. Its managed
stack begins at `GlyphRun.IPortableNativeGlyphRunSource.TryGetPortableNativeGlyphRun`.
Neither that frame nor the passing, heavily traced replay identifies the invalid
access. [Issue #278](https://github.com/wieslawsoltes/LibreWPF/issues/278) remains open.

Native-crash replay now omits renderer tracing and the scheduled live snapshot.
It requests one separate first-chance capture for an actual access violation
above the first 64 KiB, preserving the OS-provided operation, accessed address,
instruction and stopped thread. The helper continues with
`DBG_EXCEPTION_NOT_HANDLED`, so application handlers and any later second-chance
event retain their original behavior. An ordinary managed exit-one failure
still receives the existing traced/live-snapshot diagnosis.

The optional capture uses the same five-second/32 MiB normal-dump budget and
original 110-second diagnostic child bound. It never retries the capture. Raw
bytes remain outside uploaded evidence until PID, exception, access parameters,
intrinsic architecture and faulting context validate. Receipt schema 3 separates
first-chance evidence from unhandled-crash and exception-free live-stack evidence.
A recovered first-chance fault explicitly reports `provesUnhandledCrash=false`.
The original acceptance child, payload checks, assertions and 120-second bound
are unchanged; its failed receipt is still written before the separate replay.

This follows Microsoft's documented [debugger exception handling](https://learn.microsoft.com/en-us/windows/win32/debug/debugger-exception-handling)
and [access-violation parameters and CLR null-address behavior](https://learn.microsoft.com/en-us/shows/inside/c0000005).
No external implementation was copied. Thirteen offline controls pass. The
native ARM64 helper compiled locally with warnings as errors and passed its
callback, ordinary exit, handled exception, null AV, live snapshot, handled
non-null AV and unhandled non-null AV controls. Native x64 and full application
CI remain required.

A separate local Windows ARM64 run of the unchanged Showcase sources against
whole-qualified `ffa1f5dd` packages completed in 51.85 seconds, with all four idle
intervals, restoration, clean exit and unchanged payload hashes. It used no
clipboard or synthesized input. That different qualified package run does not
explain or replace the failed `f9507dc67` run.

## Bounded native compute attribution

The ordinary managed-failure replay requests `PROGPU_NATIVE_TRACE_COMPUTE=1` in its private
child environment. ProGPU #221 supplies bounded owned-pipeline, workgroup and
submission records for both native providers. `nativeComputeTraceRequested`
records the request, not proof that a particular native payload emitted records.
The ordinary acceptance run rejects an inherited enabled compute trace before
payload inspection or child launch; its original uninstrumented result remains
authoritative. Existing log-tail, snapshot, child and application deadlines are
unchanged. Neither encoded work nor a queue submission proves GPU completion.

WPF Build 36411654709 at `68c11d9c` remains failed at ARM64 resize. Its replay
keeps the owner loop running and records 296 pending surface-configuration polls,
five native MIL frames and no sixth frame after resize. The 40,047 ms normal dump
establishes WARP compute execution, but its resource heap is absent. Do not name
the shader from generated instruction patterns alone. The new trace must be
consumed with the exact successful upstream producer and original application
before attributing that work; it does not itself fix or qualify resize/idle.

## ARM64 result after real-fence surface reconfiguration

Exact head `800d15fd5`, Build `36340593124`, passes the x64 native Showcase,
SDK consumers and Linux popup gates, but the ARM64 application still fails.
Its original PID 9648 exits 1 without an access violation or child timeout and
restores the UI. Initial/scrolled observations retain stable source identity,
4→4 / 5→5 presentations and 2007.7321 / 2000.6860 ms intervals. They still record
2687.5 / 6093.75 ms of process CPU and 192736 / 186248 allocated bytes. Zero extra
presentations are not proof of idle CPU or complete issue #179 qualification.
After resize, the native update-boundary callback does not enter within the
unchanged 9.6-second bound. This is a new observed failure, not a crash-resolution
claim or permission to extend timeouts. Evidence is retained under
`artifacts/idle-current-integration/fence-ci.YrqQsfZg/` in the diagnostics worktree.
The independent `602a59864` Build `36341507005` repeats this ARM64 failure:
PID 5688 exits 1, restores the UI and records no crash or child timeout. Its
initial/scrolled intervals retain 4→4 / 5→5 frames, with 2750 / 5500 ms process
CPU. The callback timeout must not be treated as a one-off passing-run retry.

Source inspection found an independent idle allocation: event-pump and render
callbacks constructed native-loop trace strings even when tracing was off.
Those hot call sites now use an enabled-gated interpolated string handler so
the state reads and formatting expressions are not evaluated when disabled.
Enabled text/formatting and the existing logger remain unchanged. Controls
exercise skipped side effects, throwing expressions and enabled formatting,
and guard actual pump/render call-site wiring. They do not qualify native GPU
completion, resolve the ARM64 callback timeout, or establish near-zero app CPU.

The later `a73f6f344` Build `36344542300` passes x64 but still fails the ARM64
native-resized boundary with `TimeoutException` and restored UI. Initial/scrolled
observations remain 4→4 / 5→5 frames, with 2453.125 / 6437.5 ms process CPU and
129328 / 147224 allocated bytes. The diagnostic artifact `10941206347` has SHA-256
`5363b0ad0a0fae6d670c5b0a1f7e75335d675af35887fc34b84fe39a6653b3b2`.
Disabled trace formatting was therefore not sufficient to resolve the stall.

The host render boundary now explicitly opts into ProGPU's native queue deferral
overload. If actual completion is pending, it preserves the existing presentation
retry and returns before texture acquisition; a later normal render tick retries
configuration. It does not pump a nested event loop, spin until quiet, reset the
idle observer, infer completion from counters or extend any deadline. Original
one-shot ProGPU presentation calls keep their synchronous behavior. This removes
the host's explicit synchronous resize-drain path, but attribution of the observed
timeout and successful native application/idle qualification remain unproven.
The ProGPU and canonical LibreWinForms dependency links stay aligned; no packages
from the failed `a73f6f344` Build may be staged as qualified release inputs.

## Diagnostic scope

### Pending-queue localization

At `36397d1549c67d76db6e3bc20af395cba4d16c7f`,
[Build 36389031885](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36389031885)
passes the x64 application but fails ARM64's passive resize. Both the original
and the diagnostic child exit normally with code 1; no native exception was
captured. The replay repeatedly reaches the render boundary for 884×601, while
the native queue is pending and the last presented frame remains 744×521.
This excludes a lost resize request in that replay, but does not identify the
unfinished GPU operation. CPU encode-stage times are not GPU completion times.

The failure-only replay now requests one external live-process `MiniDumpNormal`
at 40 seconds. It uses only the debugger's owned child handle, without injecting
a breakpoint, calling renderer APIs or extending the existing 110/120-second
bounds. The five-second/32-MiB dump cancellation limits still apply. A child
which exits sooner produces no snapshot. This observation can perturb the
replay; it never runs in or qualifies the original acceptance test.

Live stacks are explicitly distinct from crashes: `ExceptionParam=nullptr`, a
separate raw directory, and `stackSnapshot` receipt with actual elapsed time,
PID, architecture and hash. Publication requires no exception stream, complete
thread contexts/module tables, matching intrinsic architecture, bounded normal
dump flags and exact PID. The existing exception dump validator still requires
its unique, correlated exception. Partial/raw snapshots stay outside uploaded
evidence. Live snapshots do not claim an atomic frame or prove which queued
command owns a sampled thread; correlate them with the replay's phase journal.

The installed-tool Windows controls include an owned sleeping fixture whose
ordinary exit must remain 17, with a valid live stack and no false crash dump.
Both architecture jobs exercise it before publishing the diagnostic helper.
This adds failure localization, not a rendering fix or release qualification.

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
