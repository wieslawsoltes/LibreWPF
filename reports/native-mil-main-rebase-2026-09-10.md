# ProGPU latest-main rebase

PR [ProGPU #155](https://github.com/wieslawsoltes/ProGPU/pull/155) is merged.
Its merge commit `73cda9a5243e3bea75e0c8c2fc4d4ecdaf44889d` was fetched and
confirmed as latest main again immediately before publishing this integration.
All 1,173 non-merge commits from the former feature tip `549f1b55` were replayed
onto that base. Historical main-merge commits were flattened; features were not
squashed away. The resulting ProGPU integration is `2a998c86b669dda259a638470491440c1478a71f`.

The prior interrupted merge is superseded, not a build input. Original dirty
ProGPU files and deleted performance artifacts remain untouched. Rebase and
native/managed compilation used isolated linked worktrees. The original local
ProGPU feature checkout still points to the old tip to preserve that work;
the committed LibreWPF gitlink points to the rebased commit. Do not run a hard
reset, forced submodule update or pull-merge over that dirty checkout.

## Integration and source migration

Acceptance application: source-built LibreWPF Showcase alongside managed portable
mode, with upstream CAD consumers preserved. Blocking source path: the shared
mesh wire/shader ABI and `WpfNativeMilSceneCompiler` material-mode selection.
Bounded outcome: latest-main integration without conflating CAD and WPF modes;
optional viewport integration is not a new prerequisite for core delivery.

ProGPU ABI 4 combines CAD image/material fields and WPF light ranges in a
264-byte wire record, uploaded as a 272-byte aligned GPU record. Face/specular
flags no longer overlap CAD texture flags. Native pipelines retain CAD strips,
edges and depth ownership together with WPF culling, light and brush resources.
Managed CAD edges retain the complete expanded 560-byte record stride.
The shared staged winding shader retains main's rational curves. Brush validators
retain both CAD hatch sets and source outside-color policies. Generated contracts
and MIL coverage were refreshed. See ProGPU's
`docs/native-mil-main-rebase-2026-09-10.md` for the detailed resolution record.

This LibreWPF update replaces ambiguous numeric shading modes with typed
`WpfLighting=7` and `Flat=2`. It retains the uniform-light path when the explicit
range is empty and preserves emissive materials as unlit. Matching source
compiler assertions migrate with the adapter.

## Compilation, not qualification

- ProGPU renderer/test/sample dependency graph: Release, zero warnings/errors.
- ProGPU native benchmark sources: Release, zero warnings/errors.
- New CAD test/sample/native-compiler graph: Release, zero warnings/errors.
- Native macOS ARM64: both wgpu-native and Dawn, SDK static libraries and all
  configured test/sample targets compiled with Apple Clang C++20/Werror.
- LibreWPF adapter and test sources: isolated checkout with ProGPU `2a998c86`,
  repository SDK 11 preview, Release, **116 warnings, zero errors**, 24.64 seconds.

The native build uses the explicit build-only lane/header compatibility mode;
this is not C++ module, Windows, Linux, runtime or performance evidence. All
application/verifier/benchmark execution and CI review remain in the final
qualification phase. The old package feed does not contain this ABI migration
and must not be reported as qualified or mixed with the new managed assemblies.
Core delivery row 2 remains open; broader DirectX/Direct2D/Win2D scope remains
explicitly deferred. A successful rebase does not make either PR merge-ready.
