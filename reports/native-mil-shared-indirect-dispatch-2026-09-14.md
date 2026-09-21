# Shared indirect query dispatch — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, pointer and region input. The bounded
blocking path was the ordered-query probe's native-only WebGPU dispatch, which
could not safely forward a borrowed Dawn or browser device. ActivityMonitor work
is excluded, as requested. Broader compatibility expansion remains deferred.

ProGPU commit `9944c50c09294d58a38ea764786b3519ad596a7c` is pushed to PR 139.
The shared typed API now carries the actual pass/buffer identity and u64 offset;
Dawn/C++ use the owning provider, and browser uses one fixed-size command in its
existing packet. Inexact JavaScript offsets reject before packet mutation, without
CPU argument readback. The test now uses this shared API and can borrow a real
Dawn Metal context. No runtime defaults or WPF dependency pins changed.

## Evidence

- 120 complete result-buffer comparisons pass independently against the original
  ProGPU shader/reference on wgpu-native Metal, Dawn Metal and system ARM64 WARP.
- The Windows run at `C:\ProGPU.OrderedQueryStages-System-Strokes-SharedApi`
  exited 0; all four actual native/compiler/system module paths were observed.
  Compiler: pinned DXC 1.8.2502.8; WARP: the system DLL, not a development package.
- Windows stdout SHA-256:
  `299e072e3d3e5609fb3c682910117894b48fe8008eda34fe249f6a1e301dcc5b`.
  Published ProGPU.Backend.dll SHA-256:
  `765afc3d84bed1fbc6617e0a1e730e92155b2c2b1e2701e151e081f00810d9ae`.
  Logs/publication remain under `artifacts/native-query-stages.j19Hlu`.
- Eleven actual BrowserWebGpuApiTests pass through a focused linked project;
  the full source-only test project remains blocked by absent submodules.
- Nine tests execute the actual JavaScript decoder with test resources. This
  verifies transport, not browser device/runtime qualification.
- The C++ provider fixture passes strict C++20/Apple Clang compilation and exact
  handle/offset forwarding, missing-procedure rejection and scope restoration.
  Full CMake native-provider tests now include it; this local standalone run is
  not a full native build. Workflow lint and diff checks pass.

## Remaining merge blockers

1. Pair managed/native product query dispatch, lazy family pipelines and
   generation-owned scratch/indirect buffers with typed execution selection.
2. Preserve pending owner-map/readback lifetimes; reject candidate overflow before
   publication and admit only supported device/storage/dispatch bounds. Cover
   multi-level/sparse trees and capacity boundaries without relaxing comparisons.
3. Complete compiler-feature packaging and Windows native package consumer gates,
   then final Showcase source/package input, popup/DPI and lifecycle validation.
4. Require final-head CI and merge ProGPU 139, LibreWinForms 29 and LibreWPF 115
   in that dependency order. All remain open drafts; none has been merged.

The prior `a1a86493` Build run has passing Linux/macOS managed and original-shader
checks, but was still running at this checkpoint. New-head CI must finish too.
The known Windows package failure is not resolved by the isolated dispatcher.
Details and public-contract provenance remain in ProGPU's
`docs/native-ordered-hit-query-stages.md`.
