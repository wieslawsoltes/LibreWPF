# Native query compiler/runtime separation

Acceptance application: `ProGPU.Wpf.ShowcaseApp`; action: first native point and
region input. ProGPU `848f22ad` shares exact triangle predicates across corner
lanes and the single-point wrapper. It also records actual loaded Windows runtime
versions and separate submission/readback milestones in the package fixture.

## Implementation and validation

Quad containment uses two vector triangle calls instead of eight scalar calls;
triangle-cap containment uses one instead of four. The original edge-sign test,
boundary and degenerate behavior, both windings, query geometry, source identity
and result ordering remain unchanged. No CPU geometry or shader-family removal
is introduced. All 4,096 GPU differential lane comparisons, 158 focused tests
and 20 native CTest entries pass. Both providers compile on macOS/MSVC. The full
hash-checked Metal consumer passes; the renamed geometry fixture/resource also
passes its 26-test focused rerun. See ProGPU's
`docs/native-hit-query-segment-lanes.md` for provenance, research and complexity.

## Windows differential

All four comparisons use the same current native renderer/shader, SHA256
`e696e6a9809f82d5baa5d45c3fcabbf11a19d7888e6c05a7cc663f3893337d52`.
DXC rows use the existing exact-ABI optional-feature wgpu-native build and explicit
diagnostic managed compiler selection; they are not shipped compiler defaults.
Actual compiler/runtime module paths are observed in the child processes.

| Compiler configuration | WARP runtime | First point submission | Result |
| --- | --- | --- | --- |
| Shipped FXC | System 10.0.26100.9278 | 25,457.121 ms | Access violation before first readback |
| Shipped FXC | Development 1.0.20 | 24,363.115 ms | Entire owner fixture passes |
| Feature-enabled DXC 1.8.2502.11 | System 10.0.26100.9278 | 743.437 ms | Access violation before first readback |
| Feature-enabled DXC 1.8.2502.11 | Development 1.0.20 | 985.959 ms | Entire owner fixture passes |

On development WARP, FXC bounds/ellipse submission takes 216,465.624/78,582.698
ms; DXC takes 1,478.972/1,023.666 ms. These are diagnostic measurements, not a
controlled throughput benchmark. Every original generation/ownership,
participation, repeated-wait and fresh region-first assertion remains enabled.
The compiler-dependent latency is distinct from the system runtime's execution
failure. The large DXC difference justifies reproducible compiler-feature and
configuration work before further small geometry-expansion refinements.

All listed processes are terminal. The paired FXC script/session 56461 exits 1
because its system control fails, while its development process exits 0. The
DXC development session 83147 exits 0; system session 70722 reports consumer
`-1073741819` and outer exit 255. The preceding segment-only development fixture
(session 36961) also finishes successfully. Do not restart those handles.

Scripts and logs remain in `artifacts/native-core-validation.GwKsGq`, including
`test-owner-query-triangle-lanes.ps1`,
`test-owner-query-development-warp-dxc.ps1`,
`test-owner-query-system-warp-dxc.ps1`, `dxc-query.stdout.log`, and
`dxc-system-query.stdout.log`. No system DLL, VM setting, global compiler default
or SDK admission was changed. Development WARP is testing-only and excluded from
product distribution.

## Authoritative CI and remaining work

Repair Build `34781150565` at `0514d72f` is terminal failure. All other jobs pass
except Windows consumers. ARM64 job `103792662164` passes native rendering, then
exits 127 after first-query submission. x64 job `103792662237` passes point work
and submits a bounds query in 144,479.732 ms; it then remains in the native wait
until cancellation at the job limit. It is not still compiling an ellipse query,
and no timeout/cancelled result qualifies a package. The newer shader head still
requires its own current-head CI.

Next work: make the optional compiler feature and its selection reproducible and
configurable without changing the pinned native ABI; independently resolve
supported Windows runtime execution. Preserve both renderer modes, source-owned
native queries, real callback completion and all final package/application gates.
Do not distribute the development runtime, replace the native index with managed
geometry, or convert a failed query into a successful miss. No dependency pin or
PR merge advances on these diagnostic passes. The broader DirectX/Direct2D/Win2D
scope remains deferred, not complete. Latest ProGPU main `cde81083` remains an
ancestor of the goal branch (zero commits behind at this checkpoint).
