# Source geometry-clip input — 2026-09-09

## Acceptance dependency

The existing source-built host's `NativeMilGeometryRelationSmoke` paints
`[8,8]-[152,88]` and installs the actual triangle `M8,8 L88,8 8,88 Z` as a visual
clip before selecting inside/outside it. The source utility path is connected,
but native MIL index production rejected the vector mask. This is a source-backed
native-input blocker, not a runtime failure reproduced in this batch.

## Implementation

ProGPU `fd2b8fbe` adds explicit source-geometry metadata to builder vector clip
resources. MIL geometry clips and non-axis-preserving rectangle clips publish
that declaration; generic material masks do not. Native source input now consumes
a single nonboolean intersect path, preserving line/quadratic/cubic controls,
fill rule and its own world transform. Shared NEON/SSE2 transforms map coordinates
without a scalar whole-buffer fallback or per-primitive native calls.

The actual clip segment range is shared across affected input primitives.
Bounds only prune candidates. A containing rectangle is redundant; nonredundant
rectangle/path intersections and multi-path/boolean/arc or opacity-mask coverage
remain explicit gaps rather than envelope hits or silently overwritten clips.
Source owner identity, balanced scopes and unmasked sibling restoration remain
unchanged. Partial capture never becomes a published successful index.

The managed source traversal already implements the corresponding path through
its exact geometry clip encoder; a paired regression fixture was added, not a
second algorithm. Native fixtures cover declaration rejection, range reuse,
independent clip/draw transforms, sibling restoration, polynomial SIMD controls
against a scalar oracle, and actual Visual/PushClip MIL ingress. An import-based
fixture covers the changed C++ overload. All fixtures are authored, not executed.

The [ProGPU contract](../external/ProGPU/docs/native-mil-vector-clip-input.md)
records paired source provenance, complexity, inherited design references and
remaining requirements. No foreign implementation code was imported.

## Build-only evidence

ProGPU Release test-project compilation completed with zero warnings/errors in
8.82 seconds. The fresh main fetch remains at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of the feature branch.
The MIL ledger was regenerated after the source edit; only the decoder digest
changed, to `48afa5fbc539e62abd9a8e2a522013776981289f5a5db7eb1a427dbd384104f3`.
Verification execution remains deferred.

The clean isolated macOS ARM64 and x64 C++ builds at `fd2b8fbe` each completed
all 41 compile/link steps, including both providers and MIL fixtures. Native builds
use `cmake --build ... --parallel 4` only and exclude unrelated modified native
files/deleted performance artifacts from the working submodule. The ProGPU commit
is pushed to PR #139. Modules are OFF in these macOS trees; required module
execution/qualification remains pending.

No test executables, applications, GPU/VM graphics, benchmarks, verifier scripts
or CI checks ran. Direct CMake builds do not restage packages: macOS staged
payloads still have `e814ca9c` provenance, while Windows/Linux and the complete
development feed remain at `da36a718`. Exact delivery-head platform artifacts,
package consumption and all final qualification gates remain outstanding.

## Remaining core work

Native host owner queries remain disabled. Finish the required clip compositions,
spatial masks/cache input and host query routing, alongside remaining acceptance
actions. This connection does not qualify the source application, full native
input coverage, feature freeze, merge readiness or broader Direct2D/COM/Win2D.
