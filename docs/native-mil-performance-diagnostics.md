# Native MIL performance diagnostics

Acceptance application: **ProGPU.Wpf.ShowcaseApp**. Action: qualify its warmed
presentation loop after native glyph raster retention repaired the timeout.
The blocking source path was `ProGpuWpfDiagnostics`: native hosts reported the
unused managed compositor's zero timings, draw counts and GPU-memory counters.

## Implemented timing contract

`TryGetNativePerformanceSnapshot` publishes the actual native host's latest
successfully presented frame. The value contains its presentation count,
device-recovery count, native scene update and render metrics, whether source
serialization ran, and CPU wall-clock durations for:

- the complete native host frame;
- source update/serialization;
- native MIL scene compilation;
- external-image binding and scene installation;
- surface acquisition;
- the native render/submission call;
- surface presentation.

Submission includes native uploads and command encoding. These measurements
are not GPU execution durations and must not be relabeled as separate upload
and encoding timings. Total frame time also includes host work between stages;
the parts need not sum to the total. A skipped source update legitimately has
zero duration and `SourceUpdated=false`.

One bounded lock publishes and reads the value as a unit after the host records
successful presentation. Readers never combine new scene metrics with older
timings. Failed acquisition/render paths do not publish a successful frame.
Disposal/device-target replacement invalidates the value; the new frame carries
the actual recovery count. Timing uses monotonic timestamps, with O(1) work,
no per-frame diagnostic heap allocation and no added native crossings.
These sequential clock observations have no independent SIMD workload.

The existing managed performance and memory snapshot APIs now return false
for native mode. Managed mode retains its previous metrics and behavior.
Showcase diagnoses this distinction explicitly; it cannot print another false
native performance success using idle managed counters.

## Validation

The isolated source snapshot builds the WPF bridge with one existing warning
and the source-host harness with zero warnings/errors. The full test project
build has 116 existing warnings and no errors. All 214 focused window-host
tests pass, including native publication, renderer distinction and disposal.

The actual macOS source-host gate passes viewport/images, text/document/source
input, geometry and native device recovery, then verifies the new snapshot's
frame count, recovery count, metrics and positive measured stages. One recovered
frame reported 23 commands, 20 resources and five submitted draws, with CPU
frame 7.594 ms, source 0.168, compile 0.073, install 0.041, acquisition 0.205,
submission 6.993 and presentation 0.031 ms. This one-frame diagnostic result is
not a matched Release performance comparison or final package qualification.

An initial diagnostic mirror placed a guard in the wrong repeated source block;
the real host gate rejected it. The mirror was corrected to match the product
files before the successful run. The working checkout's unrelated older physical
dependency mix also fails compilation; it was not rewritten. Qualification
continues using the explicitly identified isolated source graph.

Showcase also compiles with zero warnings/errors against the rebuilt bridge in
an explicitly isolated diagnostic reference/assembly overlay. It completes the
live input actions and then rejects the incomplete performance gate as intended:
frame 65 reports genuine CPU time 22.773 ms, compilation 17.754 ms, native
submission 1.850 ms and 2,292 commands/118 draws. It no longer reports zero
native work as a successful performance result. This run fails the overall
qualification gate until the memory/report work below is complete; it is not a
package closure or performance pass.

## Native memory and report connection

ProGPU `7ffe85d2` supplies the original C++ inventory through its generated
`NativeGpuMemorySnapshot` contract. See
[native ownership and evidence](https://github.com/wieslawsoltes/ProGPU/blob/7ffe85d2d5deff299e4340960d3bbef7654f81ca/docs/native-gpu-memory-diagnostics.md).
The host's `EnableNativeMemoryDiagnostics` defaults to false. When enabled, one
inventory runs on the actual render thread after successful native presentation,
before publishing the paired performance snapshot. `GpuMemory` is nullable;
an uncaptured frame must not inherit old memory. Inventory CPU duration is
reported independently and included in total host time. Ordinary frames gain
no native inventory crossing or allocation. Snapshot reads remain O(1) and
cannot call WebGPU from a foreign thread. Existing disposal/recovery reset the
whole published value.

Showcase now chooses a separate native report after the same live input actions.
It restores the previous capture setting in finally, warms 16 frames and measures
120 presentations with the existing 300-by-2-ms polling bound. It checks actual
scene/generation, engine identity and recovery count, finite stage durations,
nonempty native work and the existing endpoint known-owned GPU growth limit of
1 MiB. Peak ownership is recorded separately, including pending batches. Opaque
texture storage or borrowed views reject memory qualification until their
respective accounting contracts exist. No physical-residency claim follows.

The native report emits p50/p95/p99 for source, compile, install, acquisition,
submission, presentation, inventory and complete host CPU time, plus process CPU,
heap/working set, managed allocation and native buffer/texture counts. Submission
is not split into fabricated upload/encoding timings. Native atlas-growth and
raster-submission counters are not inferred from the managed compositor; exact
atlas/raster retention remains covered by ProGPU's independent renderer gates.
Sample arrays are allocated before timing; diagnostic sorting is bounded to
eight sets of 120 dependent values, not a compute fallback or renderer algorithm.

The source bridge builds with one existing warning, the source harness and
Showcase with zero warnings/errors, and all 214 focused host tests pass. The real
source-host device-recovery gate also passes the new inventory/scene identity
checks. The diagnostic graph uses the earlier isolated source snapshot with
current product-file overlays and the ProGPU 7ffe85d2 managed/native artifacts;
it does not change the working checkout's mixed physical submodules or indexed
pins. One initial Showcase rebuild restored an older packaged PresentationFramework
and failed the scroll test; its hash differed from the current source assembly.
That attempt is not qualification evidence. A later rebuild likewise restored
old native dylibs and rejected a batch before diagnostics; restoring the exact
current pair removed that artifact mismatch.

The corrected diagnostic app completed live input and all 120 measured frames.
One run observed buffer bytes 90,259,480 at the start, 129,954,680 at peak,
and total owned bytes 95,339,616 at the end versus 98,669,552 initially.
Texture bytes remained 8,410,072 across six textures. Pending raster batches
grew from 27 to 63 at peak. Native `submit` retains these until periodic actual
completion observation; this is evidence of transient retained storage, not a
proven unbounded leak. The initial report candidate incorrectly applied the
existing endpoint limit to the transient peak. The final report preserves the
original endpoint comparison and reports the peak separately; it does not
increase the threshold, discard pending bytes or retire work during inspection.
The final diagnostic run exits zero after every live input action and 120 measured
frames. Native owned bytes are 93,281,184 → 93,290,800 (9,616 bytes growth), with
peak 138,364,752 and 63 pending batches. Six textures remain tracked. CPU host
p50/p95/p99 is 23.098/26.228/45.690 ms; compilation 17.821/18.181/18.563 ms;
native submission 2.079/2.593/24.733 ms; inventory 0.012/0.019/0.031 ms. It
reports 2,292 commands and 118 draws, 497,770,008 managed bytes allocated over
the measurement (4,148,083.4/frame), and working set 548,487,168 → 545,619,968.
These are diagnostic observations, not a final Release baseline or speed claim.
The allocation rate and retained-batch peak need matched profiling.

The sample was built with `ProGpuWpfRendererMode=NativeMilWgpu` and
`ProGpuWpfNativeMilHitTesting=true`, then run with
`PROGPU_HIT_TEST_EXECUTION=ordered-stages`,
`PROGPU_WPF_SHOWCASE_LIVE_VALIDATE=1` and
`PROGPU_WPF_SHOWCASE_PERFORMANCE_VALIDATE=1`. It used Debug Showcase with
Release source PresentationFramework/bridge and current ProGPU native backend
assemblies/dylibs restored after the build. Product/report source hashes match
their isolated copies. This explicit overlay does not qualify the unchanged
indexed dependency pins or SDK package graph.

## Required next work

### Release profiling and failed-run diagnostics — 2026-09-14

Release Showcase now builds with the same native source overlay and completes
all live input plus 120 presentations in an uninstrumented baseline. Compile
p50/p95/p99 is 17.730/18.297/18.429 ms, host CPU 23.035/26.004/47.196 ms,
with 4,144,731.53 managed bytes/frame. This remains a diagnostic graph, not final
indexed packages. The initial no-restore Release build lacked an apphost; using
the intended DLL launch with `UseAppHost=false` builds without warnings/errors.

Matched Time Profiler, Allocations, Metal System Trace and EventPipe artifacts
are retained under `/Volumes/1TB-macOS/progpu-native-release.KYesd6`. ProGPU's
shared C++ scene builder was reserving exact size-plus-one capacities and moving
existing records repeatedly. Its geometric-preflight change preserves serialized
output and publication/error ordering; it does not change WPF invalidation or
source export. Native CPU hotspot attribution is documented in ProGPU's
`docs/native-scene-builder-capacity.md`.

Both the original and candidate native binaries can still fail the existing
1 MiB endpoint-growth gate, including without Instruments. Retained buffers vary
with periodic queue retirement, reaching 63 batches and 138,364,752 known-owned
GPU bytes; six textures remain at 8,410,072 logical bytes. This is not evidence of
an unbounded texture leak or a memory improvement. The snapshot getter still
does not poll, drain or mutate ownership.

Showcase now prints the measured timing/allocation report **before throwing** on
that memory failure, explicitly labeled `failed memory gate`. The same growth
condition, exception, identities, 120 real presentations, capture restoration
and deadlines remain intact. It never prints success for a failed run. This
diagnostic change prevents a memory failure from discarding the already collected
latency evidence. Managed reporting remains unchanged.

The changed reporter compiles in Release without warnings/errors. Two sequential
baseline/candidate pairs use the same Showcase assembly (SHA-256
`99036df722fead184d2cb8b956284c5381dfb734abc882c5f7f57ac41fc9ea88`).
Baseline compile p50 is 18.163/17.767 ms; candidate 3.673/3.684 ms.
Process CPU over 120 frames falls from 3,585/3,391 to 1,658/1,666 ms.
Host p95 does not improve (26.346/25.751 versus 29.958/29.378 ms), with increased
surface-acquire time consistent with changed presentation pacing. Managed
allocation stays about 4.14 MB/frame. The baseline memory gate fails/passes;
both candidate runs fail and print the diagnostic before throwing. These are
compiler measurements, not a complete performance or application pass.

Default Windows package failures also repeat in ProGPU Build `34803203731`:
x64 ellipse summary/list disagreement, ARM64 exit 127 after bounds submission.
Both explicit ordered package lanes and full-capacity differentials pass, but
that does not waive defaults or qualify automatic selection, source packages or
ordered PR merges.

Run matched final Release Instruments/counter measurements, investigate sustained
allocation/retention behavior, and finish exact package/platform qualification
and qualified dependency pins.
This is a diagnostic connection, not a measured performance improvement. The
managed report remains unchanged and native mode still rejects its legacy APIs.

Separate merge blocker: ProGPU Build `34797950771`, Windows x64 package consumer
job `103841226346`, passes ordered queries but fails the default system-FXC
ellipse participation case (`flags=C0000001`): summary hit count is one while
the returned list count is zero. The adapter is Microsoft Basic Render Driver.
Do not waive the exact-result check or switch defaults merely to pass CI.

## Explicit completed-memory checkpoints — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, warmed presentation after all live input
actions. The blocking path was the endpoint comparison of submitted-frame
inventories containing different amounts of in-flight work. The earlier raw
1 MiB endpoint gate passed or failed on the same binaries depending on where
periodic native retirement fell. The renderer's allocation/retention policy is
unchanged by this diagnostic correction.

`TryPollNativeMemoryCheckpoint` explicitly calls the existing native compositor's
latest submission token and **nonblocking** completion poll on its owner thread.
ProGPU C++ alone observes completion and retires the corresponding retained
resources. Only then does the host read the native inventory. A checkpoint
requires a captured presented frame, matching engine/scene/generation and recovery
identity, and zero retained batches. Rendering/recovery-in-progress, unavailable
hosts, pending work and mismatched snapshots cannot produce a checkpoint. The
native provider preserves wrong-thread and device-loss errors. No timer-based
completion, blocking native wait, CPU query fallback, cache purge or source-local
resource release is introduced.

The checkpoint carries the original submitted-frame snapshot **and** completed
inventory as separate values. Ordinary snapshot/inventory getters remain read-only
and the saved presented frame is not overwritten. This adapter adds O(1),
allocation-free identity checks and native calls only during explicit diagnostics,
not normal frames. It introduces no new numerical kernel or SIMD fallback; managed
rendering and its existing report remain unchanged. C++ and managed consumers both
retain the existing ProGPU completion/retirement contract; no renderer fork exists.

Showcase polls through its existing source-dispatcher/native-loop wake helper,
without requesting a new render. It retains the 300-attempt, 2-ms polling bound.
Completion endpoints enclose the 120 timed samples and remain outside their CPU,
wall-time and allocated-byte measurements. The source dispatcher may still present
additional frames while polling: an initial candidate incorrectly required equal
last-sampled and completion presentation counts. The diagnostic run rejected
278 → 286, with the same engine/scene/recovery and a later matching generation.
The final report records these two ranges explicitly, rejects stale/recovered/
different-scene endpoints, and never relabels later presentations as timed samples.
The original 1 MiB limit now compares completed owned working storage. Raw submitted
bytes, completion-frame submitted bytes and sampled in-flight peaks remain visible;
this neither caps transient memory nor claims complete driver-residency coverage.

Validation:

- Release bridge builds with its one existing unused-event warning; Showcase
  and source-host harness build with zero warnings/errors. The test project has
  20 existing analyzer warnings; all **225** focused host tests pass. Identity
  fixtures cover missing capture/presentation, engine, scene, generation, recovery
  and pending batches without fabricating actual GPU completion.
- The real macOS source-host gate completes checkpoints before and after forced
  native device recovery. Both engines have live owned buffers, zero retained
  batches at completion, and unchanged submitted-frame snapshots; recovered
  identity differs. The full existing source/input/retention checks also pass.
- Release Showcase with explicit ordered queries passes all live input actions
  and 120 timed samples, presentations 154 → 274, completion frames 154 → 282.
  Completed owned storage is **9,289,344 → 9,289,344 bytes**, 30 buffers/six
  textures and zero retained batches. Submitted storage ends at 124,023,040 bytes;
  sampled peak remains **138,364,752 bytes / 63 pending batches**. No lower
  physical-memory or reduced managed-allocation claim follows.
  Compile p50/p95/p99 is 3.754/4.134/4.362 ms, host 19.406/30.116/34.941 ms;
  process CPU 1,695.314 ms, wall 2,639.519 ms, allocation 4,145,573 bytes/sample.
  These are diagnostic measurements, not a matched package performance verdict.
- The same Release binaries also pass with **automatic Metal single-pass queries**:
  timed presentations 156 → 276, completion through 284, completed storage
  **9,287,580 → 9,287,580 bytes**, 28 buffers/six textures and zero pending
  batches. The sampled in-flight peak remains 138,362,988 bytes. Compile
  p50/p95/p99 is 3.693/4.105/4.271 ms; all live input actions pass and the process
  exits zero. This keeps the normal Metal default in the diagnostic coverage.

Logs and the exact assembly-overlay runner are retained under
`artifacts/native-completion-checkpoints.bSQpun/`. The source graph remains the
explicit isolated diagnostic snapshot, with current WPF overlays and ProGPU
`0ac6a5ff` managed artifacts / `21e9ed86` native build. It is **not** an exact
indexed NuGet package qualification. SHA-256:

| Artifact | SHA-256 |
| --- | --- |
| Showcase DLL | `5ed2a88acf289a919088ceb76c6038894c8d7d241d8d6a87688e9728718e574d` |
| ProGPU.Wpf DLL | `594d92191c22d9f445dfbb3a5ddd3928ede942d2f51e320c75edac8751c3ec96` |
| ProGPU.Backend DLL | `5e3a44758d96256643a4ad26efa0927d292c6771b359c948cdff4732ba844d2b` |
| ProGPU.Backend.Native DLL | `a73048e6cb9d6f9b848307d8c0992a145e0ef4a8aa29b90e17da6f3230f97915` |
| Native dylib | `e76956d7db8c2a8d97f341b9c96ef0aa67f3ffb0e351fa1120e532b7f81e3e9b` |

The earlier default-FXC blocker above now has a separate, evidence-backed ProGPU
policy fix: actual owned D3D12/FXC devices select ordered GPU stages automatically;
Metal/Vulkan/DXC/unknown devices preserve single-pass automatic selection and
explicit preferences remain authoritative. Independent full-capacity differentials
and the actual owner-query probes pass in both Windows VM architectures. See
[ProGPU's selection/evidence record](https://github.com/wieslawsoltes/ProGPU/blob/0ac6a5ff79d79e7a6e5a2e5b0488955cfb2256d7/docs/native-ordered-hit-query-stages.md).
Original explicit single-pass FXC failures are not declared repaired. The current
exact-head CI/package gates, final matched Release/platform acceptance and ordered
dependency merges remain required; these diagnostic results do not waive them.
