# Windows compiler runtime package — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, startup and ordinary native pointer/region
queries. ProGPU `dec74b5b` adds `ProGPU.Backend.Dx12`, the optional pinned Windows
WebGPU/DXC runtime package shared by managed and C++ renderers. ActivityMonitor
and broader Direct2D/COM/Win2D expansion remain outside this finish batch.

The package contains x64/ARM64 feature-enabled WebGPU libraries, signed/hash-pinned
compiler payloads, receipts and original notices. It selects only the known Silk
Windows native asset during build/publish, never caller-owned assets, NuGet-cache
files or system DLLs. Explicit RID admission, pre-pack completeness and WARP
exclusion guards are implemented. Compiler/query defaults remain unchanged.

## Evidence

- The actual DX12 NuGet restores and publishes the expected ARM64 library hashes.
  Its full native consumer passes on system WARP without an external compiler
  directory: retained MIL pixels, owner/generation isolation, participation and
  region-first queries. All four native/compiler/system module paths are checked.
  The renderer itself is still project-reference C#/C++ in this local run, not
  the complete CI NuGet graph.
- Successful stdout SHA-256:
  `094e4fc40c85637cfe761a6f321574152c20ab42a93ce04035d2589e71f28f75`.
  Artifacts: `artifacts/dx12-package.toCGR2`.
- x64 dependency cross-build takes 2m14s and passes exact ABI/PE checks. Its DLL
  SHA-256 is `5777347a165be3d640424b48f2eab8e11523d678c381e9ca1373fdd7ff9101c3`.
  Cross-compilation is not x64 runtime qualification.
- Nine runtime-input and three MSBuild selection tests pass. Missing executable
  RID and missing package RID reject; package completeness now rejects before
  NuGet creation. Both-RID package production has zero warnings. Workflow lint
  and all four new/changed production/test PowerShell parse checks pass.

## Remaining finish gates

Build CI now produces both compiler-runtime payloads and the optional NuGet, then
runs added full Windows x64/ARM64 JIT and NativeAOT package consumers. Existing
default gates remain intact. Final-head CI must complete; the prior `089e9120`
had no failed jobs but native lanes were still running at this checkpoint.

Complete the exact CI-built package graph, x64/NativeAOT/hardware qualification,
release/default integration and Showcase source/application input, popup, DPI
and lifetime checks. The independently reproduced stock-FXC X3511 remains open.
No dependency pins advance and no PR merges occur until the required gates pass.
Merge order remains ProGPU 139 → LibreWinForms 29 → LibreWPF 115.

## Hosted CI and release integration follow-up

ProGPU `4b6d9cbe` fixes the missing shipping-manifest entry found by documentation
CI, adds the native-assets package to portable/release packing, verifies both RID
inventories/notices and requires added Windows DX12 JIT/NativeAOT release gates.
The project audit count increases by exactly one, to 81; no verifier is bypassed.

At the previous `dec74b5b` head, x64 compiler-runtime production passes. ARM64
fails before runtime tests: hosted LLVM 22.1.8 makes the pinned binding generator
emit opaque C records (247 missing-field errors). The documented upstream Clang-22
typedef regression matches this failure. Build-time libclang is now separately
pinned to signed ClangSharp 18.1.3.1 packages with author/package/DLL hashes,
matching the generator that passed in the VM. No upstream implementation or ABI
changes, generated-binding patch, runtime libclang distribution or system change.

Both signed libclang payloads stage successfully; six existing compiler-input
tests, the actual two-RID package verifier and Build/Release workflow lint pass.
Full local documentation verification still requires the absent ACadSharp
submodule. Fresh hosted docs, ARM64 generation, complete package graph and actual
Showcase qualification remain required; no dependency pins or merges advance.

## Complete renderer package graph and NativeAOT

Both hosted compiler-runtime builds at ProGPU `4b6d9cbe` now pass in Build
`34793857889`, including the repaired ARM64 binding-generator lane. Hosted Docs
`34793857865` and the full local documentation verifier pass. One source assertion
still expected the pre-DX12 release job dependency list; it is updated to require
the new gate and all existing gates. All 53 original source diagnostics tests
pass in an isolated test project; the full local test project additionally needs
the separate Microsoft UI XAML checkout and is not claimed green.

Renderer packages from Build `34792388706` (`089e9120`, artifact `10329321304`,
version `0.1.0-preview.3034.ci`) pass the full Windows ARM64 consumer in both JIT
and NativeAOT. The optional DX12 NuGet is locally packed from the verified two-RID
payloads at that version. All renderer assets now come from NuGet: no project
references or DLL overlays. Both runs use explicit DXC/ordered stages and system
WARP, without an external compiler directory.

Retained rendering is 38 resources/11 draws/174080 coverage. Original owner/
generation, 16 repeated waits, participation and region-first checks all pass.
JIT verifies five loaded renderer/compiler/system module paths. Stdout SHA-256:
`3accf5b12b53ca805195e688f4ed998df971eb60f19611e0c96fbdcfac260e5e`.
Artifacts: ProGPU `artifacts/native-package-3034*`. Both fresh VM stages remain
available; no system DLLs, VM settings or user applications changed.

This closes the project-reference-only package gap. Final-head CI, x64 runtime,
hardware/default selection and Showcase source/application checks still gate
ordered merges. LibreWinForms PR 29 is green at its unchanged head, but waits
for ProGPU; dependency pins and all draft states remain unchanged.

The same complete NuGet graph also passes the full consumer on Apple M3 Pro/Metal
with explicit ordered stages; the Windows-only optional package leaves its native
asset selection intact. At `ab32ad25`, the assertion repair is pushed and Docs is
green; Build `34794383097` remains pending. The prior Windows/Linux source-test
failures both identify that same corrected assertion.

The older `089e9120` hosted x64 stock-runtime consumer is now terminal red in job
`103823672111`: FXC X3511 forced-loop-unrolling rejection at first owner-pipeline
creation, followed by the pinned native library's NulError panic. Its independent
path/raster/native-submit/cubic diagnostics all pass. Linux x64/ARM64 package jobs
pass at that head. This preserves a concrete Windows default-compiler blocker,
not a generic CI wait or a waiver justified by the passing explicit DXC lane.
