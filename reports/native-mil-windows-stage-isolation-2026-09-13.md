# Windows native package and query-stage isolation

## Current merge status

### Accelerated hosted isolation follow-up

ProGPU now includes demand-driven native path pipelines at `5e3bbc27` and
current-package failure diagnostics at `5a3b6bfd`.
The original failing package remains `e56d45c5` / `0.1.0-preview.3000.ci`;
dependency pins have not been moved merely to restart the downstream failed gate.
All three goal PRs are conflict-free; none is merged or fully qualified.

[Native path diagnostic run 34768876404](https://github.com/wieslawsoltes/ProGPU/actions/runs/34768876404)
passes on both hosted Windows architectures using that exact package. The
canonical ordinary-path shader produces `(255,0)` interior/exterior coverage;
the same-command partial R8 atlas copy reads `(255,0)`, and an untouched pixel
reads `0`. This narrows the basic raster/transfer stage without qualifying
cubic math, native material/buffer setup or fragment sampling. The new test also
passes on Metal and both VM adapters with the original packaged wgpu runtime.

The follow-up separates a direct native rectangle draw from the unchanged MIL
cubic assertion, each in a fresh process. Metal passes the direct native path.
Completed hosted results in
[34769078839](https://github.com/wieslawsoltes/ProGPU/actions/runs/34769078839)
pass all three stages on ARM64. On x64 the raw/copy stage passes, but both the
direct native rectangle and original MIL cubic are entirely black. The x64
failure does not require MIL or cubic geometry. The same package's x64 DLLs
pass both native probes under x64 .NET in Parallels with its normal adapter;
see `artifacts/native-windows-consumer.akdIwM/native-path-x64.log`. This is not
full x64 consumer or application qualification, nor a cleared driver-cache test.
This diagnostic workflow reuses earlier artifacts explicitly and logs their
provenance; it never replaces the current-head package consumer. See ProGPU's
`docs/native-path-first-frame-isolation.md` for the exact stages and boundaries.

Superseded full Builds `34768264510` and `34768876402` were cancelled to release
runners after the latest head was pushed. Neither is a passing gate. Current
full Build `34769078820` subsequently failed browser evidence readback at
`map-requested` without browser errors; it and superseded `34770221857` were
cancelled after the replacement head was pushed. Final-head Build `34770390199`
is required. The completed original Build remains
40 green checks and two Windows package failures as detailed below.

The native path initialization previously created seven coverage pipelines and
all three extra signed shader modules even for an ordinary rectangle. It now
creates only the requested nonempty batch families and caches them on the same
engine, shared between ordinary paths and clip paths. Entry points, bindings,
sampling, submits, owner input and fallback policy are unchanged. Failure occurs
before consuming a borrowed encoder. Both native providers compile locally;
all 20 C++ tests pass. Rebuilt Metal libraries pass fresh direct/cubic probes
and existing ordinary, forced-inline, forced-staged and vector-clip comparisons.
No latency improvement or hosted failure repair is claimed before measurement.
Windows MSVC now builds both providers from an archive of `5e3bbc27`; the
native DLL SHA-256 is
`E423C947F43771A567F78EEB41E8EA8511DD7FD614609F5B42AEB4734FFD372E`.
The isolated consumer retains package 3000's Backend and original wgpu runtime
(`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDBC`)
with the normal Parallels adapter. Separate direct-path and cubic processes pass
in 31.722s and 25.705s, exit 0. These end-to-end process durations are not isolated
compiler timings or a controlled old/new performance comparison. Build/probe
log: `artifacts/native-windows-consumer.akdIwM/lazy-path-arm64.log`.
The rebuilt browser contract also passes its unchanged 120-second deadline and
existing masks/image checks; ProGPU log: `artifacts/lazy-path-browser.log`.
Hosted and final package results remain pending.

Historical-package diagnosis is now manual-only with an explicit run ID. The
full Build's failure-only Windows step runs all three stages against its own
package, collecting every exit while preserving the original failed consumer.
No required test, deadline, assertion or adapter policy was weakened.

Additional local query bisection retains original geometry classifiers:

- Removing tree traversal/result ordering alone does not resolve the crash:
  both direct rectangle classifiers fail with DXC in compute and fragment stages.
- Specializing that direct fixture to its actual rectangle kind passes in both
  stages with DXC, and compute also passes with original FXC.
- Specializing a real stroked-line path fixture to path-stroke kind still fails
  in compute with DXC, but fragment succeeds with both DXC and original FXC.
- Sharing the rectangle stroke piece loop preserves the original sample counts
  and cap predicates. The full mixed-primitive rectangle query passes with DXC
  in fragment, but compute still crashes. Original FXC fragment times out at the
  unchanged 30-second readback deadline and then exits with an access violation.

These remain isolated diagnostic shader substitutions under
`artifacts/native-windows-consumer.akdIwM/QueryShaderProbe`, not production changes
or a qualified alternate-stage fallback. In particular, the DXC-only success is
not sufficient to enable that path with the shipped original runtime/compiler.

### Original package gate

ProGPU `e56d45c504ccc6d7e4fd683ff259adb40f6fc759` remains current with
`main` (zero commits behind the fetched branch). Build run
[34763887821](https://github.com/wieslawsoltes/ProGPU/actions/runs/34763887821)
has 40 successful PR checks and two failures: native package consumers on
Windows x64 and ARM64. Both native compiler/runtime build lanes, browser,
Linux/macOS consumers and System.Drawing pass. This is not green CI.

LibreWinForms PR29 at `f268f73c1` is fully green. LibreWPF's SDK smoke at
`46d0f4d6c` stops because the exact upstream ProGPU Build failed; the downstream
package/startup/platform consumers are skipped, not qualified. No independent
WPF implementation error was reported by that smoke job. No PR was merged.

## First-frame failure is independent of queries

Both hosted Windows consumers fail before invoking hit testing. The retained
cubic fixture's unchanged diagnostic reports:

```text
RGBA=(0, 0, 0, 255); deviceLost=False
backend=D3D12; adapter=Microsoft Basic Render Driver
coloredPixels=0; coloredBounds=(300,150)-(-1,-1)
commands=3; draws=1; coverageBytes=9728; vertexBytes=224; uniformBytes=224
```

The entire target is black, not merely the asserted rectangle sample. The
rectangle is a separate contour in the same path, so one path draw is expected.
Do not call this a query failure, change the pixel oracle, discard the cold frame,
add a successful warm-up substitute, or increase a readback timeout.

CI uses clang-cl 20.1.8; the prior staged VM native build uses MSVC 14.51 with
`/O2 /Ob2 /DNDEBUG`. An isolated comparison replaced only the two native DLLs in
the existing ARM64 software-adapter consumer with the exact CI native artifact.
It passes cubic and retained rendering, submits a point query in 20,917.857 ms,
then crashes with `0xC0000005` after 1m57.104s. Therefore the native compiler
difference alone does not explain CI's missing ink. The VM's software graphics
stack is not equivalent to the hosted runners' stack.

## Shared shader fault bisection

An isolated `QueryShaderProbe` uses ProGPU's existing managed device index,
query/result records, six buffers, shader resource and query entry point. A
diagnostic-only reflection hook substitutes its process-local resource before
static initialization and verifies that the selected shader was installed.
No such hook, shader reduction, compiler selection or adapter override is added
to production. Probe inputs are the existing rectangle/ellipse/line fixture at
point `(50,50)`; a passing probe must return owner 30.

With the preserved DXC-enabled dependency and forced Microsoft Basic Render
Driver, the unchanged compute shader crashes. Reductions that omit path strokes,
omit endpoint-cap checks, or replace precise geometry by bounds complete. These
reductions are fault-isolation controls, never qualified feature replacements.

Behavior-preserving attempts to share endpoint evaluation, predicate its result,
defer endpoint checks, guard the end index, branch tangent selections, vectorize
triangle sign tests, or consolidate stroke-piece loops still crash. Reducing arc
tangents, either or both endpoint tangents, triangle caps, or one endpoint's check
also does not resolve the fault. None of those speculative refactors is committed
to the product. Their results do not prove a particular cap's mathematics wrong.

A separate original-runtime validation build enabled the D3D12 debug/validation
flags. It reports write-combine performance and downlevel-capability warnings,
but no captured validation error before the access violation. Source changes
were preserved as a local patch, reverted, and the ordinary Backend rebuilt.
No VM setting, execution policy, global debugger or shipped dependency changed.

## Equivalent fragment-stage probe

One fullscreen triangle over a one-pixel, single-sample target invokes the
unchanged `query_scene` exactly once. The same six storage buffers retain the
same query, index, ordered writer and readback. This is a stage-eligibility
experiment, not a source-local hit index, CPU geometry fallback or product API.

| Probe | Compiler/runtime | Result |
| --- | --- | --- |
| Point, Metal fragment | Shipped Metal runtime | Pass, owner 30 |
| Point, WARP fragment | Isolated DXC-enabled runtime | Pass, owner 30 |
| Point, WARP fragment | Original shipped FXC runtime | Pass, owner 30 |
| Rectangle, WARP fragment | Isolated DXC-enabled runtime | Access violation |
| Ellipse, WARP fragment | Isolated DXC-enabled runtime | Pass, owner 30 |
| Rectangle, WARP compute | Isolated DXC-enabled runtime | Access violation |
| Rectangle, WARP compute | Original shipped FXC runtime | Device lost during readback |

The original-runtime point fragment probe completes in 21.491 seconds including
compilation, with one visited node and three candidates/precise tests. The DXC
point and ellipse probes complete in 1.260 and 2.040 seconds. These are diagnostic
observations with different compiler inputs and overlapping application work,
not controlled benchmarks. Only simple fixture outputs were asserted; path,
clip, owner-list, native C++ integration and full region equivalence are not
qualified by those passes. Rectangle compilation/execution remains a blocker,
so no automatic or forced product fallback is enabled from this evidence.
The original-runtime rectangle compute probe reports map failure `0x887A0005`
and `WebGPU Status: DeviceLost`, then exits with an unhandled managed exception;
it is not the same observed exit as the DXC access violation. All stage probes
and the exact package consumer are terminal; no diagnostic process is left running.

## Exact package comparison and retained artifacts

The unmodified current package closure `0.1.0-preview.3000.ci`, produced by the
current ProGPU Build, completes with the current consumer source in
`C:\ProGPU.Native.PackageConsumer-akdIwM-package3000-arm64`. It selects the normal
Parallels D3D12 adapter without diagnostic overrides and exits 0 after
12m23.361s. Cubic coverage, retained rendering, owner/generation isolation,
participation and fresh rectangle-first/ellipse-first queries all pass. This
qualifies this exact native package-consumer scenario, not full Showcase startup,
other platforms, hosted WARP or acceptable cold-interaction performance. The
first rectangle submission takes 233,601.270 ms; compilation latency remains open.

Package SHA256 values:

```text
Backend        f6fb19c0708a1946e04d78158f28fda480467eec55050ba1fc46ed02f4d4a6e0
Backend.Dawn   aa10de95dd217b4e809ff3d4e92dcfedd5eaeb1da3715120c7a2c6ef2a6caa66
Backend.Native c471aff9e8603202d69a3a14e4aad042dd2a8bff0234d7d4a9a19c9f49f3efee
```

All probe sources, staging scripts, compiler/runtime hashes and logs are under
`artifacts/native-windows-consumer.akdIwM`. Key files are `QueryShaderProbe/`,
`query-probe-*.log`, `warp-validation-diagnostic.patch`,
`ci-e56-native-runtime-comparison.log`, `ci-e56-native-runtime-stack.log`,
`ci-e56-native-packages/` and `package3000-arm64-consumer.log`.
The failed hosted logs are retained as `/tmp/progpu-e56-package-win-x64.log`,
`/tmp/progpu-e56-package-win-arm64.log`, and `/tmp/librewpf-46d0-sdk-smoke.log`.

The next core work remains the hosted software-renderer first frame and complete
GPU query support, followed by exact package/application/platform qualification
and green checks before the documented ProGPU → LibreWinForms → LibreWPF merges.
Successful point-stage probes do not close rectangle queries or authorize merging
red checks. General Direct2D/Win2D/COM expansion remains deferred, not completed.
