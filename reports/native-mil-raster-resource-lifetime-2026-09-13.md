# Native raster-resource lifetime and merge qualification

## Acceptance path and diagnosed boundary

Application: `ProGPU.Wpf.ShowcaseApp`. Action: present its first native MIL frame.
Blocking shared path: C++ path rasterization, coverage upload and vector draw.
Broader Direct2D/COM/Win2D expansion remains deferred under the core delivery plan.

The exact package 3005 Windows comparisons isolate early raster-resource release:

| Independent comparison | Windows x64 | Windows ARM64 |
| --- | --- | --- |
| Keep references through exact submission completion | Pass | Pass |
| Release only command-buffer reference before completion | Pass | Pass |
| Release five raster buffers and their bind group before completion | Black frame | Access violation |

Evidence: [ProGPU run 34775504926](https://github.com/wieslawsoltes/ProGPU/actions/runs/34775504926).
The original native cubic fixture remains black on both architectures in that run.
This isolates the lifetime-sensitive boundary outside the C++ renderer; it does
not identify an individual buffer or establish a driver-internal cause.

## Implementation and local qualification

ProGPU `f5dcfb15` introduces submission-bound leases for uncached native path,
clip and glyph staging. Active recording survives split submissions; borrowed
semantic encoders retain resources through their future submission. Existing
periodic polling and bounded draining own retirement. Latest-token explicit waits
and engine disposal release completed resources. No per-draw wait, frame retry,
extra submission, altered shader, CPU fallback or relaxed assertion was added.
`7e6b749c` avoids scanning the retirement list on ordinary lease publication before
completion, retaining amortized constant-time publication.

Both desktop C++ providers use the policy. Managed buffers already use deferred
disposal. Browser WebGPU retains its existing encoded-reference ownership without
creating an undrainable native synchronous-poll queue. Cached path/glyph replay
does not allocate a new staging lease.

- Both C++ providers compile on Metal and Windows ARM64/MSVC.
- All 20 native CTest cases pass, including recording/completion/cancellation
  lifetime regressions; native generated-contract verification passes.
- The full Metal native package consumer passes original drawing and native
  owner/generation/participation/region checks, including after the publication
  optimization.
- Actual source-host/device recovery passes on Metal with the retained backend.
- Original rectangle and cubic pixel fixtures pass on the Parallels Display
  Adapter and Microsoft Basic Render Driver with the rebuilt native backend.

The Windows comparison uses the existing package-3000 managed/runtime assembly
closure with the rebuilt C++ library. The software-only diagnostic selects WARP;
that Backend assembly is not shipped. These are staged comparisons, not exact
final-head package qualification. Native DLL SHA256:
`079FB052B7CA73F95AAB2BFF1F1F27D9EFA4A464599F512B5C1ACA315059CF1E`.

## Remaining merge gates

Final-head [ProGPU Build 34775916222](https://github.com/wieslawsoltes/ProGPU/actions/runs/34775916222)
is running for `7e6b749c`. Superseded builds were cancelled to free runners; none
of those cancellations count as passing qualification. Dependency pins are not
advanced before the required upstream checks pass.

The staged Windows ARM64 source host passes source text, document and geometry
checks, then fails with a native-loop cleanup exception. Its existing native-loop
trace is being used to recover the primary failure. The original 15-second
recovery deadline remains unchanged. Earlier x64 recovery timeout is not closed
by the independent pixel probes.

The trace run subsequently reaches a real native presentation, drains the close
request and exits with the original `NativeMilHostDeviceRecoverySmoke.RunAsync`
line-29 timeout while waiting for dispatch of the injection callback. The earlier
cleanup exception is not evidence of a fixed recovery path.

The full staged WARP consumer subsequently passes the original cubic and retained
MIL scene (38 resources, 11 draws, 174,080 coverage bytes), then exits with access
violation `-1073741819` after native owner-query submission (21,507 ms), at 1m38s
total. This is a separate open query blocker. Query buffers and bindings are
engine-owned through the pending request; inspection does not find the same
temporary-raster release pattern there. Do not claim the raster repair fixes
software-adapter query execution or replace it with an unqualified fallback.

PRs 139 (ProGPU), 29 (LibreWinForms), and 115 (LibreWPF) remain unmerged. Required
order is upstream qualification/merge, exact downstream pins, downstream CI and
package/application qualification, then dependent merges. No full-goal completion
or one-hour merge guarantee follows from the staged passes.

Local logs are under `artifacts/native-core-validation.GwKsGq`, including
`native-raster-retention-windows.log`, `native-raster-retention-warp.log`,
`native-host-raster-retention-metal.log`, and the ARM64 host/trace logs. ProGPU
build, contract and consumer logs are in its worktree's `artifacts` directory.
VM lifecycle, configuration, installed runtimes and driver settings were unchanged.

## Query execution and source-startup follow-up

The isolated zero-segment canonical-shader diagnostic passes a point query on
Microsoft Basic Render Driver but crashes before rectangle completion. Compiling
the rectangle pipeline alone succeeds in 22.802 seconds without creating query
buffers or submitting commands. A separate cold rectangle probe retains every
buffer, binding, pipeline, encoder and command through readback: it creates the
pipeline in 23.722 seconds, submits at 23.744 seconds, then exits with access
violation before readback completes. The matching Metal probe returns all three
expected owners. Thus this query failure does not reproduce the temporary raster
reference-release boundary fixed above.

A diagnostic replaces only the repeated rectangle edge/quad corner call sites
with bounded loops over the same predicates, vertices and short-circuit order.
The zero-segment WARP fixture then returns all three owners in 31.972 seconds.
The full shader and remaining families still require validation; neither this
diagnostic rewrite nor zero-segment specialization is enabled in production.

LibreWPF now records a missed initial 15-second presentation prerequisite before
attempting device recovery. Once a prerequisite fails, the harness closes and
reports that failure instead of launching subsequent native queries which could
hide it behind another crash. Successful runs retain every existing input and
recovery assertion. The explicitly enabled native-loop trace records invariant,
monotonic elapsed milliseconds and includes native window initialization.
Two focused source-graph tests, source-host compilation and Metal host/recovery
pass; these are not Windows qualification.

The first timed Windows run presents after 7.354 seconds measured from input
attachment, then crashes in `BeginHitTest` from the harness rectangle owner query.
A live stack confirms that call path. That timestamp excludes earlier window/
composition initialization and cannot establish the original first-frame deadline
was met. The trace now starts before native initialization to measure it directly.

Code inspection also found eager managed glyph/path pipeline compilation during
native-host composition-target construction. ProGPU now defers those pipelines
until actual atlas rasterization, retaining shader algorithms, captured execution
policy and cache/disposal contracts. Its native providers already create the
corresponding resources on demand. See ProGPU's
`docs/native-atlas-pipeline-initialization.md`; Windows timing is still required
before claiming a startup fix.

During this follow-up the Windows VM became suspended before a probe started.
It was resumed without reset or configuration changes, and guest command access
was verified. The failed transport attempt is not test evidence. Logs include
`query-zero-segments-compile-warp.log`, `query-retained-bounds-warp-resumed.log`,
`query-quad-loop-warp-dispatch.log`, `source-first-frame-timing-windows.log`, and
`source-first-frame-timing-stack.log` in the existing validation artifact directory.

### Measured atlas startup comparison

ProGPU `f4002192` passes 78 focused Release atlas/resource tests. In the matched
Windows ARM64 staged source host, native window initialization drops from
34.526 seconds (baseline) to 0.635 seconds (lazy atlases). The baseline explicitly
misses the original 15-second initial-frame prerequisite and presents only at
40.251 seconds. With the new Text/Vector assemblies, the first native presentation
occurs at 9.147 seconds; injected device loss is recovered and the replacement
device presents at 15.123 seconds, about six seconds after the first frame. The
existing recovery assertions report success. No deadline, shader or renderer
selection changes were used. The paired Metal source-host/recovery run also passes.

These are staged runtime measurements, not exact final-package or full application
qualification. The Windows run continues into native owner-query validation, which
remains open. The same native DLL and host setup are retained; the logged SHA256
values identify the changed managed assemblies. Logs:
`source-initialization-baseline-windows.log`, `source-lazy-atlas-timing-windows.log`,
and `source-lazy-atlas-timing-metal.log`.

The baseline also exposes a fixture-owned dispatcher leak: creating the paginator
for deliberate synchronous block/inline/table rejection enables background
pagination by default. Its later queued unsupported-layout exception can interrupt
the unrelated native host and produce a secondary Silk cleanup failure. The
rejection fixture now disables its own background pagination before the same three
`GetPage` assertions. Product pagination defaults and unsupported-layout rejection
are unchanged; no queued exception is globally swallowed.

The full canonical shader with the diagnostic rectangle loops still fails: its
pipeline takes 125.247 seconds to create and readback reaches the unchanged
30-second timeout. The process subsequently exits, confirmed by guest process
inspection. The loop rewrite and zero-segment specialization remain diagnostic-only.

### Hosted first-frame gate remains red

The x64 package consumer for `7e6b749c`, Build `34775916222`, job `103779010680`,
fails the original independent rectangle ink assertion in the cubic fixture:
RGBA `(0, 0, 0, 255)`, zero colored pixels, no reported device loss, D3D12 Microsoft
Basic Render Driver. Its separate native-path and native-cubic probes also fail.
The canonical managed vector, native-layout/binding and indexed native-submit
probes pass; the original native path's prepared payload hash still matches the
reference `37CF2B2338D40B07`. Thus the raster retention repair and staged MSVC/WARP
passes do **not** establish resolution of the hosted native first-frame failure.
That rendering gate remains open alongside native query completion; the atlas
startup improvement closes neither. The ARM64 package job is still running at
this checkpoint. The exact log was downloaded to
`/tmp/progpu-7e6b749c-package-win-x64.log` for continued isolation.

### Terminal query result and atlas configuration isolation

The source-host execution above is now terminal (host command exit 255). Its
log records fatal `0xC0000005` in `NativeMethods.BeginHitTest`, called through
`TryQueryHitTestBoundsOwners` during `ValidateNativeMilHostResult`. Do not restart
or describe that process as still qualifying. First-frame/recovery assertions
passed before this crash; the complete source-host gate did not.

Build `34775916222` is also terminal: all jobs except the two Windows native
package consumers pass. ARM64 passes the original cubic and retained rendering
fixtures, then exits 127 after native query submission; x64 still fails drawing.
The ARM64 log is `/tmp/progpu-7e6b749c-package-win-arm64.log`.

ProGPU commit `d142b7cb` isolates two actual differences between its raw canonical
diagnostic and native atlas: CopySrc usage and explicit/default view descriptors.
Three independent variants preserve the original target pixel checks, canonical
shaders, prepared payload and retained references. Only legally unavailable
atlas-storage readback is omitted for the CopyDst-only variants; target and raw
coverage checks remain. Release compilation and all three Metal probes pass.
Manual diagnostic run `34778761952` applies these to the completed failing Build
package. It is not final-head qualification and changes no product renderer,
fallback policy, dependency pin or release gate. See ProGPU's
`docs/native-path-atlas-diagnostics.md`.

### Atlas result and packaged shader provenance

Diagnostic run `34778761952` completes on both architectures. The baseline and
all three atlas variants pass exact target pixels. Both direct native path
probes still produce black interiors; x64's original cubic also fails, while
ARM64's cubic passes. The direct ARM64 outcome differs from the earlier Build's
direct-path pass, so neither that earlier pass nor the isolated atlas variants
establish reliable native rendering. Do not change product texture descriptors
based on this comparison: their isolated differences do not reproduce the fault.

The completed failing package is `0.1.0-preview.3013.ci`, from source
`7e6b749cc608a8b878c4ac4c63c51db2e2620721`. A read-only archive inspection confirms
the complete canonical Vector (124,648 bytes), PathRasterizerCommon (27,331)
and PathRasterizer (14,862) source text in the managed Backend assembly and both
Windows native DLLs, after CRLF/LF normalization. Native prefix/main composition
also matches. This rules out those embedded-source mismatches, not runtime shader
compilation or execution defects. Package SHA256 values:

- Backend: `f6124344ad29df2ac2c3c222cdc8dae1f7a29cd2c272e6f6421127a9e7fb3b34`
- Backend.Native: `d777967c4479c9f2b6ca5ae10069dde712540f7749f01ef3b9ac85e5a6341e35`

The inspection script is retained as
`artifacts/native-core-validation.GwKsGq/inspect-packaged-path-shaders.py`.
ProGPU `5ba70df9` next isolates native compute layout minimum sizes and the
ordinary path's unwritten combine buffer. All three new variants compile and
pass unchanged Metal pixel/coverage checks; Windows diagnostic run `34779073956`
is live. The superseded queued Build `34778760253` was cancelled to release
capacity; it is not passing qualification. The renderer build and latest-head
Build remain active. No product fallback, dependency pin or merge gate changes.

### Completed encoding and raster retirement isolation

Raster diagnostic `34779073956` completes: baseline and all three raster
variants pass on both Windows architectures. ARM64 also passes the actual native
frames in that run, while x64 fails both. No isolated minimum-binding or unused
combine-buffer change reproduces the x64 failure.

Encoder run `34779972312` also completes. Both runners pass the canonical raw
reference with a finished encoder released before submission, including the
variant releasing the submitted command buffer before the exact-token wait.
All target, raw coverage and atlas checks pass. Both actual native path/cubic
frames fail on both runners. This excludes those isolated reference releases as
the reproducer, not all native resource lifetime defects.

The actual lazy-atlas renderer Build `34777843000` is now terminal: only the two
Windows package consumers fail. x64's direct native path remains black, while
its original cubic readback also reports DeviceLost. ARM64 passes initial
rendering and exits 127 after native owner-query submission. Other jobs pass;
the complete Build is red and cannot qualify dependency advancement or merges.

ProGPU `f51cd965` adds the bounded retirement comparison in its existing package
consumer. One raw probe releases raster buffers/bindings only after a confirmed
exact-token wait and before target readback. Another executes the actual native
path but defers the native retirement poll until after the existing synchronized
target readback, with cleanup in `finally`. Both preserve the exact target pixels;
the raw probe additionally checks the original atlas samples. It cannot reread
the deliberately released coverage buffer. Product rendering, lifetimes, shader
selection and readback deadlines remain unchanged. Both variants compile without
warnings/errors and pass on Metal with payload `37CF2B2338D40B07`. Windows run
`34780425843` compares them against the same completed failing package 3013;
it is diagnostic-only, not current-head CI qualification.

### Observed completion, not timeout-based retirement

Run `34780425843` is terminal. x64 job `103786318970` passes the retained raw
baseline, but reports DeviceLost after raster references are released following
the blocking poll. Its original direct native path is black; the identical draw
passes when native retirement is deferred through synchronized target readback.
ARM64 job `103786318840` passes all probes. This is timing-sensitive execution,
not a reason to replace exact pixels or accept an intermittently green frame.

The pinned wgpu-native revision selects wgpu-core 0.19.4 commit
`87576b72b37c6b78b41104eb25fc31893af94092`. Read-only inspection confirms that its
blocking maintenance path advances retirement after the internal timed wait
without checking its fence-completion boolean; nonblocking maintenance reads
actual fence progress. See the upstream
[five-second timeout defect](https://github.com/gfx-rs/wgpu/issues/4589).

ProGPU `0514d72f` implements paired managed/native sleeping completion waits
around nonblocking polling. Native hit-test waiting observes the existing map
callback state; pending cleanup avoids the same timed poll. This is original
ProGPU synchronization code, not a copied or modified dependency implementation.
No shaders, CPU fallback, application deadlines or package gates change.

Both C++ providers compile; all 20 native CTest entries and 49 focused managed
tests pass. The actual rebuilt Metal DLL and consumer-local DLL share SHA256
`0e4764dd18334b4d1a5e16daf221dabab0cad26cfad7654c4ffb589c06a7674f`;
that consumer passes original frames and all native input assertions. A build's
initial stale consumer-local DLL was detected by hash comparison and explicitly
replaced before this final run. Native generated contracts also verify.

Manual Windows comparison `34781154788` runs a bounded nonblocking-fence probe
against the original failing package; final-head Build `34781150565` is separate.
The Parallels CLI inventory confirms the existing Windows VM running with Tools
installed, no active dotnet/native build process, and unchanged configuration.
`artifacts/native-core-validation.GwKsGq/build-fence-completion-windows.ps1`
rebuilds the two native providers in the existing task-owned MSVC cache and stages
an isolated software-adapter consumer. It retains the earlier diagnostic managed
adapter selector, so it is C++ repair evidence, not final managed-package
qualification. Windows query completion, source host and all final gates remain
open until their actual runs complete.

### Fence repair Windows result and remaining query failure

Run `34781154788` completes. Its x64 old blocking-retirement probe crashes with
`0xC0000005`; the actual-fence variant passes exact target/atlas checks after
5,977 ms and 536 sleeping polls. ARM64 also passes the actual-fence variant
(4,836 ms, 306 polls). Both old actual-native baseline frames are black, while
both deferred-retirement diagnostics pass. The run remains red because it
intentionally includes the failing historical-package baselines.

The VM compiles both providers with MSVC and passes the native internal fixture.
Its rebuilt C++ DLL has SHA256
`902431121dda4d2b189efbf2300950b05486f1751a8eff8bfec42a11d22c6248`.
The actual direct native path passes in 31.099 seconds including cold pipeline
setup; the original cubic passes in 29.410 seconds. The full consumer passes
initial and retained MIL rendering (38 resources, 11 draws, 174,080 coverage
bytes), then exits with `0xC0000005` after native owner-query submission at
19,811.814 ms. Total process time is 89.515 seconds; the outer host command
returns 255. This is terminal failure, not a live wait or full consumer pass.

The paired completion fix therefore has Windows rendering evidence, but native
input still needs repair. All 122 managed native interop contract tests pass;
that does not supersede the actual query crash. Superseded Builds `34779068703`
and `34780421690` were cancelled, not qualified. Final-head CI, source-host and
application gates remain open; neither dependency pins nor PR merges advance.
