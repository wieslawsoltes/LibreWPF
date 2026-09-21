# Native query storage and CI follow-up

Acceptance application: `ProGPU.Wpf.ShowcaseApp`; action: first source input.
The bounded experiment moved only the canonical native query's 64-entry stack
from private to workgroup storage, preserving exact geometry, traversal order,
capacity, dispatch and readback. Both native providers compiled on macOS/MSVC.
All 174 focused tests, 20 native tests and the complete Metal owner-query fixture
passed. The current product DXC/system-WARP control still exited `0xC0000005`
before readback; the paired development-WARP control passed in full. Both
processes are terminal. **The shader experiment was reverted before commit.**
It is not a product repair, performance claim or reason to change dependency pins.

ProGPU `7aa2c352` records the exact artifacts and results in
`docs/native-owner-query-warp-diagnostics.md`. Existing experimental Windows stages
and their build cache contain experimental DLLs; do not use them as current
production evidence. The macOS native build was restored to production source;
its hash-checked full consumer passes again, including rendering and all queries.

The same commit narrows the allocation measurement in
`SemanticImageEffectBuildsWithoutAllocation`: read the counter immediately after
the builder loop, before assertions. Linux Build `34786219474` reported 720 bytes
in that fixture, with 4,576 passing tests and seven skips. The correction keeps
all 10,000 builder calls and the exact zero-byte threshold; the allocation source
is not yet proven. All 122 local native interop tests pass. Fresh Linux CI must
confirm the result; no failure is waived or treated as transient without evidence.

ProGPU 139, LibreWinForms 29 and LibreWPF 115 are open, draft and conflict-free
at inspection. They are not approved to merge from those flags. Runtime repair,
permitted compiler packaging, final-head package consumers and source application
qualification remain required before the ordered merges. ActivityMonitor is
out of scope; broad Direct2D/Win2D expansion remains deferred.
