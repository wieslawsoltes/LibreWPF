# Native MIL hit-test owner snapshot connection

## Acceptance action and source-backed blocker

Acceptance application: the existing Toolkit/AvalonDock window. User actions:
click docked content, open menus/popups and select content. Its
`TryLiveHostGpuHitWithinTarget` requires retained owner-query results. Host
`TryHitTestOwner`, all-owner and region methods currently query the managed
`ProGpuWpfCompositionTarget`, while `RenderNativeMilFrame` presents a separate
C++ native scene. Native MIL `build_scene_core` does not attach a hit-test index.
These are source findings, not an executed/reproduced runtime failure.

The bounded implemented outcome is immutable owner identity from source capture
through a successfully presented native frame and generation-qualified ProGPU
GPU query tokens. This closes a prerequisite, **not** the full input blocker.
No acceptance application was reduced and no existing gate was removed.

## Implementation

- ProGPU owns `NativeGpuHitTestOwnerMap<T>` and the allocation-free bound
  `NativeGpuHitTestOwnerSnapshot<T>`. They do not depend on WPF types.
- `NativeCompositor` records installed scene identity after successful native
  update. Scene-qualified query submission and identity checks share its render
  lock. Query tokens include scene/generation and a non-pointer compositor
  identity, so native allocator address reuse cannot admit a foreign token.
- WPF's compiler records actual source visuals against MIL handle bits. It does
  not map synthetic host/placement containers. Batches and session frames carry
  the map separately from canonical MIL; byte-identical replacement content still
  publishes a new source map. The host binds only after successful presentation
  and clears the published snapshot before scene installation and at teardown.
- Existing managed renderer queries, native pending-query errors, caller spans,
  GPU shader behavior, source text caret semantics and geometry topology remain
  unchanged. There is no new reflected owner resolution or bounds approximation.

The detailed ownership/cost contract, primary cross-engine references and
remaining implementation sequence are in
[ProGPU's design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
The design adopts retained scene/spatial ownership and explicitly keeps shaping,
text affinity and exact geometry independent. No third-party implementation was
copied. Owner dictionaries are reference metadata, not a new numeric CPU fallback;
all existing GPU-first and intrinsic-SIMD requirements remain in force.

## Authored coverage and build evidence

ProGPU unit cases cover copied entries, duplicate/null rejection, signed ID bits,
same-ID owner replacement, default snapshot rejection and token identity.
WPF cases cover distinct source visuals producing identical MIL bytes and
owner-surface popup source identity without synthetic placement owners.
The existing native package consumer now includes a real GPU query fixture using
an explicitly constructed index. It checks two generations resolving different
owners and rejects stale submission, wrong-generation results and foreign
compositors. That fixture is not evidence of native MIL index emission.

All commands use Release, one MSBuild node and `UseSharedCompilation=false`.
The initial ProGPU `--no-restore` build failed in `ResolvePackageAssets` because
the shared intermediate assets had been written by the source-WPF SDK build.
Normal restore/build resolved that without changing product source or checks.
The subsequent ProGPU test graph compiled with 0 warnings/errors in 76.90 seconds;
the WPF test graph compiled with 116 existing warnings and 0 errors in 30.72
seconds; the package consumer compiled from project references with 0 warnings/
errors in 6.65 seconds. Final edited-source compilation is recorded below.

| Final build-only graph | Warnings / errors | Elapsed |
| --- | --- | --- |
| ProGPU.Tests, including the native owner map/token cases | 0 / 0 | 28.83 s |
| ProGPU.Native.PackageConsumer with `ProGpuNativeUseProjectReference=true` | 0 / 0 | 1.94 s |
| ProGPU.Wpf.Tests, including native source-owner and publication cases | 21 existing / 0 | 27.13 s |

The last WPF build was incremental; its smaller warning count is not a warning
cleanup claim. ProGPU implementation commit `facdc931` is pushed to the existing
[ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139). A fresh fetch
confirmed `origin/main` remains `102e39e5088b462624da6296ff70a43ed2c5d8b4` and is
already an ancestor of the feature branch. The LibreWPF source connection is
delivered through the existing
[LibreWPF PR #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).

No tests, package-consumer execution, app rendering, benchmarks, VM graphics
workloads or CI polling ran. Native binaries/packages were not refreshed: no C++
or generated ABI source changed in this batch. Existing six-RID native inputs
remain da36a718 and need exact-delivery package refresh during qualification.

## Still blocking completion

Native MIL must emit exact retained hit primitives/index with source visual IDs,
including clipping, stroke/fill semantics, cache reuse and brush-source exclusion.
Host point/all-owner/region and popup queries must then consume that index through
the presented snapshot, preserving synchronous source input contracts without
stale GPU results. Owner metadata alone cannot pass the Toolkit retained input
gate. Continue this application dependency, not general Direct2D API expansion;
feature freeze and full cross-platform qualification remain pending.
