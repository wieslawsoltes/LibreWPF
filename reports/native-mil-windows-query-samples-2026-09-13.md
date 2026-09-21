# Windows native query completion

## Outcome and limits

ProGPU's four-lane native path-query implementation passes the Windows 11 ARM64
Parallels consumer, exit 0 after 12m54.964s. It exercises native cubic coverage,
retained rendering, original owner/generation queries, point-only/region-only
participation and fresh rectangle-first/ellipse-first compositors. The preceding
point-only and family-only versions terminated with access violations. This is a
successful diagnostic comparison, not final-package or full Showcase qualification.

The source implementation is in ProGPU `dd6fb438`; `e56d45c5` adds its agent rules.
The Windows comparison stages both rebuilt native providers with the original
`0.1.0-preview.2992.ci` managed/native-dependency package. It does not replace the
WebGPU DLL with the rejected DXC experiment. LibreWinForms `f268f73c1` and LibreWPF
`4fa546869` consume ProGPU `e56d45c5` through indexed pins, leaving the user's dirty
physical submodule trees untouched. ProGPU remains zero commits behind fetched main.

## Bounded implementation

The acceptance path is `ProGPU.Wpf.ShowcaseApp` pointer and geometric selection
over presented native source owners. Three lazy query-family pipelines retain
one index and source traversal. Four independent path-fill samples now share one
segment load and curve evaluation. Rectangle corners and ellipse cardinal samples
retain independent boundary, parity and winding state; single points consume the
same algorithm with splatted coordinates. No fill rule, curve-step count, geometric
threshold, compiler default, CPU fallback or readback deadline changed.

The shader and detailed design/provenance record live in ProGPU:
[query specialization](https://github.com/wieslawsoltes/ProGPU/blob/e56d45c504ccc6d7e4fd683ff259adb40f6fc759/docs/native-mil-query-pipeline-specialization.md).
The shared engine's release-loop local was also renamed to satisfy strict MSVC
shadow warnings. CI confirms that compiler check passes on the preceding code head.

## Evidence

- All 156 focused hit-testing/shader-resource tests pass, including both fill rules,
  reversed winding, reflected corner order and mixed boundary/interior samples.
- Both native providers compile on macOS and Windows; generated contracts verify.
- The rebuilt native Metal consumer exits 0 through all query-family cases.
- All 621 local System.Drawing tests pass. Current ProGPU CI's focused allocation
  and official behavior-corpus steps pass; its benchmark step was still running.
  The preceding head's 7,296-byte allocation result is not explained by those passes.
- Current-head browser CI passes without a timeout or assertion change.
- Windows first submissions: point 19,818.134 ms; rectangle 264,084.415 ms;
  ellipse 48,782.541 ms. Reused queries take milliseconds. These are diagnostic
  timings, not a controlled performance benchmark or acceptable final latency claim.
- A live native triage dump places the main thread in `d3dcompiler_47.dll`, called
  through `wgpu_native.dll` and `progpu_native.dll`. This establishes the cold
  compilation stall location. It does not establish the earlier crash's root cause.
  The full successful sample process was also observed by a first-chance access-
  violation monitor, which ended on exit 0 without capturing a matching exception.

Reproduction scripts and logs are retained in
`artifacts/native-windows-consumer.akdIwM`. Relevant files are
`query-samples-tests.log`, `query-samples-native-build.log`,
`query-samples-native-contract.log`, `query-samples-native-consumer.log`,
`query-samples-windows-consumer.log`, `query-samples-native-stack.log`,
`query-samples-crash-capture.log`, and `system-drawing-tests.log`.
The local triage dump remains an untracked diagnostic artifact, not a PR attachment.
Microsoft-signed ProcDump was extracted into its own guest directory and targeted
only the exact owned consumer. No VM settings, global debugger registration or
execution policy were changed. Its one live capture perturbs timing; no matched
speedup claim is made. A separate software-adapter comparison is running against
the same native libraries with the earlier, preserved adapter-selection diagnostic.

## Still required before ordered merges

### Software-adapter follow-up

The software-adapter comparison subsequently fails after its first point submission:
Microsoft Basic Render Driver passes cubic/retained rendering, submits after
19,551.738 ms, then exits 0xC0000005 after 1m52s. An exact-input repeat under an
unhandled-exception monitor reproduces the crash. The triage dump shows PC 0 on
a worker thread while the main caller waits; it does not establish a specific
faulting ProGPU or driver function. Scripts, WER event and stack/register reports
are retained with the local dump `dotnet.exe_260913_170538.dmp`.

An isolated DXC comparison uses the same native query implementation and consumer,
the previously built exact-pin feature-enabled WebGPU DLL, and SDK DXC/DXIL files.
Its first point submission takes 840.777 ms, but it still exits 0xC0000005, after
6.939s. This rejects compiler selection alone as a demonstrated repair. The
temporary managed adapter/compiler patch was preserved, reverted, and the normal
Backend rebuilt successfully; no experimental dependency or selection is shipped.

The managed `TryHitTestPointUsesGpuQuadtreeAndPreciseTesting` test was then run in
an isolated Windows folder with the same software-adapter-only diagnostic and
canonical four-lane shader. Its test host also crashes. This narrows investigation
away from a C++-bridge-only defect without proving the exact shared shader/backend
cause. The shader already dispatches one invocation; a proposed 64-invocation
hypothesis was ruled out by source inspection and no such change was made.

Current ProGPU CI completes the entire System.Drawing job successfully, including
the unchanged allocation limit and benchmarks. The earlier be199695 native x64
job separately fails its `--semantic-per-point-path-guideline` rendering benchmark
before package production. That benchmark does not call the hit-query shader;
do not equate its silent child failure with the query crash without further evidence.
The current-head full native/package jobs remain required. Both software-adapter
query failure and hosted missing-ink/rendering failures remain merge blockers.

### Merge sequence

1. Resolve/qualify Windows CI's independent missing rectangle ink. The VM adapter
   pass is not equivalent to the hosted runner's older software graphics stack.
2. Finish exact-head native package consumers, required CI and downstream package
   startup/application/platform gates. No draft PR has been merged.
3. Retain the multi-minute cold region-compilation limitation in qualification;
   steady-state millisecond results do not erase it.
4. Merge ProGPU, then LibreWinForms, then LibreWPF with pins adjusted to the actual
   integrated commits and downstream checks repeated as needed.

Four superseded Build runs were cancelled to release runners (34762600632,
34761948474, 34761357934, 34763831683). Their completed logs remain useful
diagnostics; cancellation is not passing CI. The current producer and the earlier
missing-ink diagnostic run remain active. General Direct2D/Win2D/COM expansion and
other deferred original-goal requirements remain documented, not completed.
