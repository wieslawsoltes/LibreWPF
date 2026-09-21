# Native query traversal storage — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, pointer selection and geometry-region
queries. The immediate blocker is FXC X3511 during native query pipeline creation.

ProGPU's iterator had a 64-element stack in an inout aggregate. Exporting the
canonical shader through the existing pinned Naga dependency reproduces the
failure in the dynamic child-stack loop. Moving only that array to invocation-
private storage makes point, bounds and ellipse compile with the same FXC flags.
This is original ProGPU code, shared by managed/C++ providers. Traversal capacity,
order, clipping, geometry and counters are unchanged. No shared workgroup state,
CPU fallback, upstream patch, new dependency or policy/default change is added.

Evidence:

- Dense Metal: 120 complete result records/100 public ordered queries match the
  independent original reference. Sparse Dawn: 168 records/140 public queries
  match its independent original-shader reference, including counters/unused slots.
- Both providers compile with AppleClang and Windows ARM64 MSVC; 19 native CTests
  and 54 original source-diagnostic tests pass. Full documentation verification passes.
- The rebuilt native consumer passes on Metal single-pass and system ARM64 WARP
  DXC/ordered stages, preserving 38 resources/11 draws/174080 coverage and all
  owner/generation, repeated-wait, participation and region-first assertions.
- Stock FXC/single-pass completes its first point readback and 16 repeated waits,
  but crashes `0xC0000005` after the first bounds submission. Compiler repair is
  not full Windows runtime qualification. The FXC staged comparison remains separate.

Artifacts and diagnostic scripts: ProGPU `artifacts/query-hlsl`. The native DLL
overlays in these runs are explicit diagnostics, not a freshly qualified package.
The shader SHA-256 is
`1dbf5e9bab657323460d680e5417b6aa1f29b23656efca210520b1c4a23b3f4b`.
DXC successful stdout SHA-256 is
`3151874456b24e93b293c9b723887fafd21769d187315c471a731ee1f3d6f94f`.

Continue the concrete Windows bounds/runtime, final-head package/CI and actual
Showcase gates. No dependency pin, draft state or ordered merge admission changes.
ActivityMonitor and general Direct2D/COM/Win2D expansion remain out of this batch.

## Stock FXC ordered-stage follow-up

ProGPU `81ccf3fe` contains the shared storage repair. The stock Silk ARM64 WebGPU
library with automatic FXC selection and explicitly ordered queries now passes
the complete native consumer on system WARP, including bounds/ellipse and
region-first checks. It does not load a replacement WebGPU implementation or
select DXC. Stdout SHA-256:
`b649245074fa771e98f4587bb3c80ca61fae8bb0344e13a38027da5ea1ed565e`.

The independent dense Windows GPU contract also passes all 120 full result
records and 100 public ordered queries with FXC. No counter/unused slot is waived;
stdout SHA-256:
`6c050825fd0e9a4eccbaa33201d4cebe8f80ffae246b6f7f0fbad62e5da671e6`.
Build CI is extended to execute the existing full ordered-query package step
on Windows x64/ARM64 system software adapters as well as macOS/Linux. Its separate
default and NativeAOT steps/deadlines remain unchanged. A successful explicit
path does not qualify a default-policy change or waive the single-pass bounds
crash; final package, hardware and actual Showcase gates remain required.

The multi-level sparse FXC run subsequently passes all 168 complete records and
140 public queries against the independent Dawn/original-shader reference.
Stdout SHA-256:
`d48cb31aa7dcb3afce13f86a0105df9c0d0e8015820d081d66c3207279174659`.
All local native/contract/FXC processes in this batch have terminated; no live
diagnostic is being treated as completed from a log tail alone.
