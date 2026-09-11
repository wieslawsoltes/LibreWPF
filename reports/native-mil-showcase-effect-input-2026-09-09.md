# Showcase built-in effect input — 2026-09-09

## Acceptance dependency

The existing package Showcase has `ShowcaseBlurEffectBorder` (Gaussian radius 2.5) and
`ShowcaseDropShadowEffectBorder` (radius 9, depth 4, direction 315, opacity 0.55).
Source `Visual` maps input through `EffectMapping.Inverse`; these built-in WPF
effects inherit its identity mapping. Native capture rejected their PUSH_LAYER
commands despite the identity source-input contract. This is a source-backed
blocker, not an application failure reproduced at runtime.

## Connection

ProGPU `e814ca9c` adds explicit source-identity effect layer admission, limited
to unmasked SrcOver built-in blur/box-blur/shadow chains and their zero-effect
clip boundary. MIL annotates only uncached source visual effects while recording
input owners; any inner source-opacity isolation is annotated separately.
Spatial masks, cache layers and unknown mappings are not silently admitted.

Final effect clips belong to composite state, not the untruncated raster input.
The producer now carries nested world-rectangle input clip scopes, intersects
them with draw-state clips using NEON/SSE2 half-plane lanes, and preserves
Save/Restore and layer-pop ownership. Clip cache entries include both state and
layer scope. Actual four-edge clip geometry is emitted; effect padding and layer
storage bounds never become hit geometry. Logical source-image rectangles retain
these outer clips too. Rendering, shader parameters and GPU submissions are unchanged.

Managed typed source visual capture now uses the explicit
`EffectBase.PreservesSourceHitGeometry` contract: false by default, true for
built-in blur/shadow. It preserves actual commands, children, clipping and input
at zero opacity without invoking OnRender or creating a visual-size hit box.
Existing effect invalidation remains unchanged; arbitrary effects still fail closed.

The native fixture includes nested clipping, clip restoration, state reuse,
three built-in effect kinds, alpha zero and undeclared/blended rejection.
Canonical MIL scene 9817 covers the Showcase effect parameters plus zero blur.
The paired managed fixture covers source geometry and unknown-mapping rejection.
An import-based module consumer fixture covers the new public C++ enum value.
All are authored, not executed.

In-repository provenance, source/public contract research, paired applicability
and bounded O(L + P) clip storage are documented in
[ProGPU's design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
No third-party implementation was copied. The changed MIL decoder digest was
regenerated with `eng/progpu-generate-mil-coverage.py`; protocol verification
remains deferred with all other qualification.

## Build-only checkpoint

The ProGPU Release test graph compiled with **0 warnings, 0 errors, 32.57s**.
A fresh main fetch remains at `102e39e5088b462624da6296ff70a43ed2c5d8b4`,
already an ancestor of the feature branch.

Clean macOS ARM64 and x64 native compilation at `e814ca9c` each completed all
75 compile/link steps and staged both providers and native SDK inputs.
Test/sample targets were compiled, not run. The WPF Release graph compiled with
116 warnings, 0 errors in 22.66s; warnings were not suppressed or called green CI.
Modules were OFF; the import-based fixture remains for the
required final module gate. Unrelated modified native files and deleted
performance artifacts were excluded using the isolated checkout.

Source inspection identified shadow-only fields on the blur fixture descriptors;
`46004314` corrects those fields without changing product code or the MIL digest.
Both ProGPU commits are pushed to PR #139. The corrected ARM64 and x64 MIL test
targets each compiled/linked in two steps with exit 0, without execution.
This target build does not refresh or relabel the staged `e814ca9c` package payloads.

No applications, test executables, verifiers, VM graphics, performance runs or
CI polling ran. Windows/Linux payloads and the complete development package feed
remain at the earlier `da36a718` checkpoint. Exact-head final platform packages,
application behavior and all required CI remain unqualified.

## Remaining core work

Native host queries remain disabled pending remaining application coverage and
routing. Continue the same Showcase's source input path: ordinary text/chrome guideline
state, required exact geometry clips and local caches must retain source geometry
instead of leaking raster adjustments into input. Trace the actual commands and
host query lifecycle before enabling native point/all-owner/region callbacks.
The source point and geometry drawing walkers explicitly treat guideline pushes
as balanced input no-ops. Native input must preserve the unsnapped source frame,
not merely accept guideline flags after raster snapping already changed it.
Custom effect mappings and broader Direct2D/COM/Win2D work stay explicitly deferred
unless a concrete acceptance action requires them.
