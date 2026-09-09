# MVP clipped scrolling: application closure gate

## Acceptance action and gap

The existing `SelectorScrollViewer` in the complete package-mode MVP is the
acceptance surface. Required actions are wheel scrolling, clipping input outside
the viewport, and replacing content after it has scrolled. The old live check
verified routed MouseWheel count/sender/delta only. Its short content could fit
inside the viewport, so that check did not require a changed offset or a rendered
scroll update. It remains as event-routing coverage but is not scrolling proof.

The added `ValidateLiveClippedScrollAsync` operates on that same viewer/TextBlock,
not a stripped-down app, fake visual or alternate renderer. It expands the
existing details into twelve newline-separated paragraphs using ordinary source
TextBlock content, then restores the original text and offset in finally.
It does not alter window size, renderer, theme, scrolling mode or clipping policy.

## Source-backed implementation trace

No new renderer algorithm was needed for this action:

- `ScrollViewer.OnMouseWheel` calls its `IScrollInfo.MouseWheelDown` for negative
  deltas. `ScrollContentPresenter.SetVerticalOffset` invalidates arrangement;
  `ArrangeOverride` positions the actual child at the negative source offset.
- The presenter's `GetLayoutClip` returns its actual RenderSize rectangle.
  Source `UIElement.ensureClip` publishes it as VisualClip, and source `Visual`
  exposes clip and offset through `IPortableVisualStateSource`.
- `WpfNativeMilSceneCompiler.AddVisualCore` sends that source clip through
  `SetVisualClip`/typed geometry and the child translation through
  `SetVisualOffset`. Retained session deltas track those source packets.
- ProGPU native `Scene/Builder/progpu_native_scene_builder_hit_test_capture.cpp`
  retains state-frame rectangular clipping for the point/drawing primitives.
  Managed `GpuRenderCommandHitTestCache.SourceVisual.cs` consumes the same source
  local/outer clips and transformations through its retained traversal.
- TextBlock's previously connected own PointOnly rectangle does not enlarge its
  RegionOnly glyph geometry. Its point region must move and be clipped with the
  source visual, independent of whether there is ink at the probe.
- Source `VerifyScrollData`/`CoerceOffsets` owns clamping after content shrinks.
  Neither renderer should retain a stale scroll transform or old text coverage.

This is source inspection, not runtime reproduction or parity qualification.
No ProGPU code change/PR increment was required: the existing shared algorithms
are the intended implementation, and both modes now exercise the same app gate.

## Authored gate requirements

The gate now requires all of the following, without deleting earlier checks:

1. Actual pixel-scrolling content larger than the viewport, starting at offset 0.
2. A presented content update after expansion and a device-resident renderer index
   after its first real query (upload remains demand-driven).
3. Host wheel input causes a positive offset and a presented frame-state change
   without resize; the source text moves by exactly the negative offset once.
4. Visible text retains source point ownership inside the viewport. A probe
   inside the scrolled text's arranged rectangle but above the actual viewport
   does not include that text in either all-owner point or region queries.
   All-owner queries prevent another foreground visual from hiding a clip leak.
5. Shortening text while scrolled presents another change, clamps the offset to
   zero, preserves new visible input and removes old text coverage near the
   viewport bottom.
6. Finally restores source text and prior offset before later application actions.

These checks use existing typed diagnostics and source geometry. They add no
hot-path reflection, raster readback, renderer fallback or geometric approximation.
Fixture strings and diagnostic owner arrays allocate only in the live validation
lane, not production frame/input paths. No performance claim is made.

## Build-only evidence

The existing isolated complete MVP under `artifacts/native-core-mvp-build.Bb1vvF`
links the current repository MainWindow source. Builds use the root pinned SDK,
Release, `-m:1`, `UseSharedCompilation=false`, osx-arm64 and the existing
preview.45/preview.55 development feed. Managed output uses a separate directory.
The initial gate compiled with zero warnings/errors in 3.75 s. After correcting
the diagnostic ordering to inspect residency after the first query, the final
native-mode build compiled with zero warnings/errors in 3.80 s. The final
managed-mode build also compiled with zero warnings/errors in 6.49 s.

No application, test, verifier, GPU/VM workload, image/performance comparison or
CI polling executed. Automatic CI is unchanged. These builds use older package
payloads and do not prove exact-head package consumption. ProGPU remains at the
previous selection-input commit `bc635859`; no native binaries were rebuilt or
restaged for this gate-only change. Existing unrelated ProGPU dirt is preserved.

## Next application dependency

The MVP selection and ordinary clipped-scroll paths now have the identified
source connections and authored application actions. Do not extend this into
optional scrolling/geometry variants. Continue Toolkit/AvalonDock's actual native
application path, preserving paid Xceed/SciChart coverage, then feature-freeze
only when all required actions have no known blocking unsupported branch.
Final execution, image/lifetime/performance/DirectX comparisons and exact-head
packages/CI remain mandatory; this report does not close row 2 by compilation.
