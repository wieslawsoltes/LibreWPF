# Native MIL host query routing — 2026-09-09

## Acceptance action and implementation outcome

Applications: the existing source-built native host harness, package Showcase and
Toolkit/AvalonDock. Actions: point input, all-owner selection and bounds/ellipse
region selection. The source-backed blocker was `ProGpuWpfWindowHost` forwarding
these callbacks to `ProGpuWpfCompositionTarget`'s managed index in native mode.

The host now selects its presented native owner snapshot before the managed
index for all five callback families. The adapter uses ProGPU's shared GPU
Begin/Wait operations, stack/caller spans and bounded pooled expansion when an
unresolved source ID fills an initial result window. Native intersection details
become the existing neutral `PortableGeometryHitTestCandidate`; there is no
WPF-local geometry algorithm, reflected product adapter or CPU query fallback.

ProGPU `f1f5326b` adds the native metadata export and typed owner-copy helper.
Native diagnostics report actual index presence/counts/residency through the
qualified scene rather than borrowing managed counters. Reading metadata does
not create/upload the GPU index. Both native providers share the implementation.
See [ProGPU's routing contract](../external/ProGPU/docs/native-mil-hit-test-host-routing.md)
for original-source provenance, complexity and paired applicability.

## Ownership and admission

Renderer mode and `EnableNativeMilHitTesting` are frozen at host construction.
The latter requires native rendering, selects complete-index compilation and is
copied by source-window activation and separately surfaced popup creation. It
remains opt-in until application coverage is complete. Native queries without
admission throw explicitly; they no longer consult the managed index.

Successful queries release temporary snapshot ownership. A failed completion
retains its original token/map and rejects a further query until the owning
compositor is disposed. Host target teardown resets this state only after engine
disposal. Errors are not swallowed, retried through a generic recovery loop or
converted into managed hits. Existing source refresh and native device-recovery
scheduling remain authoritative.

Package-mode configuration is `ProGpuWpfNativeMilHitTesting=true` together with
`ProGpuWpfRendererMode=NativeMilWgpu`. The SDK rejects inconsistent modes/values.
The native branch of `eng/progpu-wpf-sdk-ci.sh` now requires this option; managed
qualification remains independent. The real source-built host harness also
enables it and checks that point/region input resolves its original drawing
visual and that the native index is uploaded. These gates are authored, not run.

## Authored fixtures

- ProGPU: metadata before/after GPU upload, stale-scene rejection, bounded ordered
  owner copying, unknown/no-hit/repeated IDs and both providers' C argument guards.
- WPF host: all five callback families with an absent managed index, unresolved
  top-owner expansion, native cache diagnostics, misses, disposal, explicit input
  admission and frozen renderer/input choices.
- Existing source-built host harness: real drawn source owner input after native
  presentation (also used by its device recovery lane).
- Existing SDK source-contract fixture: typed native input build/bootstrap wiring.

No fixture, sample, verifier, VM graphics workload or CI check executed. Clean
native builds use only committed ProGPU source; unrelated local native edits and
deleted performance artifacts remain excluded.

## Build evidence

ProGPU Release test graph compiled with SDK 10.0.201: 0 warnings/errors, 37.36 s.
WPF Release test graph compiled with the root .NET 11 preview SDK. Initial builds
reported 116 warnings (including transitional WinForms compatibility, unused
display-event and test analyzers), 0 errors. The final incremental test-graph build
reported 20 xUnit warnings, 0 errors, 11.52 s. The first real source-built harness
build succeeded in 69.14 s with 4 existing linked `SfntFontFace` style warnings
and 0 errors. These are compile results, not passing tests or green CI.

The final source-built harness refresh completed in 62.40 s with the same
4 warnings and 0 errors. The updated SDK external source-contract harness compiled
with 0 warnings/errors in 2.82 s. The ProGPU native package-consumer fixture also
compiled through project references using the root SDK: 0 warnings/errors,
2.48 s. It did not consume a refreshed package or execute its GPU checks.

Clean macOS ARM64 and x64 native builds at `f1f5326b` each completed all 303
compile/link steps, including both providers and configured fixture/sample
targets. They used `/usr/bin/clang++` and the existing clean detached checkout at
`artifacts/native-core-build.KvxVug/progpu`; C++ modules remain OFF locally.
The separate Dawn runtime/provider fixture is authored but not included in these
configured build targets. Final module/header/GCC/MSVC/browser gates remain.

Direct CMake builds did not stage payloads or produce packages. Staged macOS
payloads remain `e814ca9c`, and Windows/Linux payloads plus the complete 23-package
feed remain `da36a718`. Those old payloads cannot supply the new completion and
metadata exports. Exact-head package refresh remains a required delivery step.

Fresh ProGPU `origin/main` is still `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an
ancestor of the implementation branch. Both existing PRs remain the delivery
vehicles; no merge-readiness or CI result is claimed.

## Next core work

Continue actual Showcase scrolling/selection (`SelectorScrollViewer`, its existing
wheel-input gate and clipped content), tracing required source/clip/cache frames
into the native producer. Finish only application-blocking coverage and desktop
interaction/lifetime branches, then freeze and execute the complete final matrix.
Opt-in routing does not establish complete native input coverage or application
parity; it makes remaining gaps fail explicitly in the real native gate.
