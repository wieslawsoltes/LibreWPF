# Ordered native owner-query execution — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, pointer selection and geometry-region
queries against the presented native MIL owner snapshot. ActivityMonitor remains
out of scope. General Direct2D/COM/Win2D expansion remains deferred.

## Outcome

ProGPU `a1a8649378b8ca9fd798eccdf32dea3ef92072d8` adds shared ordered GPU query
stages, consolidated stroke sampling and an independent original-shader
differential. The isolated staged dispatcher now passes **120 complete result
buffers on system ARM64 WARP**, byte-for-byte against the Metal/original-shader
reference, including counters and unused slots. macOS also passes all 120 current
legacy/staged/original shader comparisons. A one-record candidate buffer fails
explicitly instead of publishing partial successful input.

This is a proven shader/probe path, **not yet a connected C++/managed product
dispatcher or a qualified Showcase application**. Default query dispatch and
compiler/adapter configuration are not switched. No dependency pin advances.

## Implementation and rejected approaches

The shared canonical WGSL now exposes ordered collection, clip classification,
primitive-family classification and ordered merge. The existing six-binding
entrypoints use the same resumable traversal without depending on candidate
storage. A separate 12-byte indirect buffer avoids writable-storage/indirect
aliasing. Original geometry, clip metadata, owner ordering and 1/16/24-piece
stroke samples remain intact. No CPU/managed geometry substitution was added.

The Windows trace first isolated the bounds path-stroke stage. Separating clips
alone still crashed. Consolidating repeated stroke intersection call sites made
that stage complete; the next comparison exposed only a precise-test count
difference (107 versus 139). Local counters alone and equivalent kind-range
classification did not repair it. Counting returned candidates outside the
iterator's nested loops then made all 120 comparisons pass. The original single
dispatch still crashed in a separate earlier run, so product selection remains
an implementation task rather than an inferred success.

ProGPU documentation: `docs/native-ordered-hit-query-stages.md` records the
algorithm, original in-repository provenance, cross-engine research, negative
results, reproduction and admission requirements. New macOS/Linux CI compares
against the exact original shader from `20bbf7f1`, preserving reference artifacts.
Existing Windows, source and native package gates remain unchanged.

## Evidence

Host evidence: `artifacts/native-query-stages.j19Hlu`.
Windows passing stage: `C:\ProGPU.OrderedQueryStages-System-Strokes-OuterCounters`.
Child 9584 completed with exit 0; all four expected compiler/backend/runtime
modules were observed. Normal execution used one submission per query, not
per-stage diagnostic waits. System files and VM configuration were unchanged.

- DXC: verified package 1.8.2502.8.
- System WARP: 10.0.26100.9278 from `C:\Windows\System32`.
- Feature-enabled wgpu-native SHA-256:
  `a0cdbedccc490377b94c4e7cf8506d65c85bbc0be1c6cf4d70d2a04c791806e3`.
- Passing probe Vector assembly SHA-256:
  `f10aa3a7295b9fdd517fa2d15441db05b927e557770bc3788a31a0a017d726e7`.
- Passing probe assembly SHA-256:
  `0ddf6d30ae40cb8b00b4fd81c80538c29e4fbe7022b0ce6f2dac3978f5222337`.
- Independent 120-query reference SHA-256:
  `d89271ad04fb1d944f86fa5352f37b360955e52d79f58dcccc3e8955c5653275`.
- Passing stdout SHA-256:
  `7da6e674bb3ef465d3261d9dd7d1bf9e01a546929d5f5e1e2a30766cfb11c951`.

The passing Windows probe predates only final diagnostic-option/output-text and
comment/documentation adjustments; exact final-head product validation remains
required. No testing-only development WARP is distributed by this work.

The full renderer test project could not compile in the recovered source-only
checkout because pinned ACadSharp and WinUI-theme inputs are absent. An initial
no-restore invocation ran no tests and is not evidence. The independent probe
builds without those inputs. `actionlint` and `git diff --check` pass.

## CI and merge state

At ProGPU `20bbf7f1`, Build `34787533894` completed all six native renderer build
lanes successfully, including Windows x64 and ARM64 and signed compiler staging.
Native package-consumer/portable-package jobs were still running at the latest
inspection. This is not a whole-workflow or final-head CI pass.

Follow-up: ARM64 package-consumer job `103810876711` subsequently failed in
`Restore and run package consumer`, after native owner-query submission
(11093.461 ms, process exit 127). Its logged system WARP is 10.0.26100.8972 and
compiler is system FXC; the independent path/cubic probes still pass. This is
not the isolated DXC staged dispatcher proved above. The x64 package-consumer
job was still running. Connect the staged product path and compiler package
before treating native-build success as merge readiness.

PR139, LibreWinForms PR29 and LibreWPF PR115 remain open drafts and mechanically
mergeable. Ordered merges remain ProGPU → LibreWinForms → LibreWPF only after
required qualification. The new ProGPU checkpoint requires its own CI results.
The source branch includes current ProGPU main; the pre-checkpoint comparison
was 1303 commits ahead and zero behind. Physical dirty submodules are preserved;
indexed dependency pins remain ProGPU `e56d45c5` and LibreWinForms `f268f73c`.

## Immediate completion queue

1. Connect staged dispatch/resource ownership in both C++ and managed consumers
   through typed native/Dawn/browser operations; retain owner-map/fence lifetimes.
2. Cover multi-level/sparse BVHs, overflow and device limits, and qualify x64/Linux
   alongside the passing isolated Metal/system-ARM64 workload. Keep lazy pipelines
   and measure actual cold/warm cost before changing automatic defaults.
3. Complete compiler-feature package selection and final package/Showcase input,
   popup/DPI/lifetime qualification, then merge the dependency PRs in order.

The broad goal remains active. This checkpoint does not claim that full DirectX,
Direct2D, Win2D or native application parity is complete.
