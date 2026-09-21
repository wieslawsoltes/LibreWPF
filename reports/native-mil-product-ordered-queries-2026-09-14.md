# Paired product ordered queries — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, ordinary pointer and region input.
The bounded blocker is the actual managed/C++ query dispatcher, not another
compatibility API family. ActivityMonitor remains out of scope.

ProGPU `089e9120` connects typed ordered-stage selection to both product renderers.
Candidate and indirect buffers belong to the retained index generation; pipelines
compile lazily for its actual primitive families. Both implementations admit actual
device limits and reject overflow before publishing owner results. Borrowed Dawn
and browser devices provide their own limit snapshots. Existing initialization
signatures, completion lifetimes and automatic defaults are preserved.

## Evidence

- Full project-reference native consumer passes on Metal and Windows ARM64 system
  WARP with the pinned DXC compiler. This includes retained MIL pixel coverage
  174080, 38 resources, 11 draws, owner/generation isolation, repeated waits,
  participation and region-first queries. It is not final NuGet qualification.
- Windows native DLL SHA-256:
  `16490f220019d0f1125349bc6ec2b1a10e11a1765529d5be75f0a787148235d5`.
  Successful stdout SHA-256:
  `fd6ad3e6bacb62772714e9eb51ed40385aaafec3bfb84a06b953ff62a9cd2eea`.
  Artifacts: `artifacts/native-product-queries.ub3DGt`.
- Dense differential: 120 complete GPU buffers and 100 public managed queries
  match on Metal, Dawn Metal and system ARM64 WARP/DXC. Sparse multi-level trees:
  168 complete buffers and 140 public queries match on Metal and original Dawn.
  Sparse reference SHA-256:
  `12e78381009b7417e38f04fd88f6f4d6b54f302b0f091a50d756956a1a658530`.
  Historical wgpu-native Metal reports incorrect constant node counters on that
  sparse fixture; this discrepancy is recorded, not waived or used as the oracle.
- Both native providers build on Apple Clang and ARM64 MSVC. All 19 native tests,
  20 focused policy/browser tests, nine JavaScript transport checks and generated
  native contract verification pass. Final focused tests and workflow lint pass.
- The matched FXC consumer fails during pipeline creation: D3DCompile X3511,
  forced loop unrolling failed. The pinned dependency then aborts while converting
  its NUL-containing diagnostic. No owner-query submission/readback completed.
  System libraries and VM configuration were not changed; no WARP is distributed.

## Finish queue

1. Complete the DXC-capable WebGPU/compiler package graph and qualify Windows
   x64/ARM64 packaged consumers. The FXC failure is still a release blocker;
   selecting DXC in a diagnostic directory is not a shipping default.
2. Complete sparse Windows, large-capacity runtime and exact final package gates.
   Linux/macOS CI now adds full ordered-query native consumers alongside the
   unchanged default/NativeAOT gates; browser transport is not device qualification.
3. Qualify automatic selection and actual Showcase package/source input, popup,
   DPI and lifecycle paths. Do not expand general Direct2D/COM/Win2D scope.
4. Require final-head CI, then merge ProGPU 139, LibreWinForms 29 and LibreWPF 115
   in order. Dependency pins remain unchanged until qualified artifacts exist.

The prior ProGPU `9944c50c` has green native builds on all six RIDs and green
managed Linux/macOS/Windows checks; package consumers were still running at the
checkpoint. New-head CI must complete independently. No PR has been merged.
