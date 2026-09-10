# Native MIL retained drawing-image updates — 2026-09-09

## Bounded core dependency

Acceptance sources: Showcase `ShowcaseDrawingImageControl` and
`ShowcaseDrawingImageBrushBorder`, and Toolkit drawing-image icons. Required action:
clear and refill the retained drawing backing an image without replacing the
image or visual identity. The host fixture exercises this content-update contract;
it does not claim those applications already expose a dedicated clear button.

Source-backed blocker: `Drawing.TryGetPortableDrawingBounds` returned false for
`Rect.Empty`, while `WpfNativeMilSceneCompiler.ResolveImageSource` required
positive available bounds for every non-null DrawingImage drawing. An empty
DrawingGroup remains a real non-null drawing, so native compilation rejected
that update. Managed image replay also classified the missing bounds as
unsupported; image-brush replay could instead inherit an incorrect skipped
status after mapping failure. This was identified by source tracing, not runtime
reproduction.

## Implemented

- ProGPU `82cbc114` documents the shared bounds availability/empty distinction.
  Latest fetched main `102e39e5` is an ancestor of the feature branch.
- Source `Drawing` publishes actual empty bounds successfully, retaining distinct
  finite zero-sized bounds and rejecting invalid metadata. It does not replace
  the DrawingImage source or manufacture pixels.
- Native compilation preserves normal drawing graph validation, then uses the
  existing C++ null-drawing image contract for known-empty content, with no empty
  image-bounds sideband. Existing C++ direct-image and image-tile paths already
  implement this no-op; no native renderer change is required.
- Managed portable image and tile replay consume known emptiness before mapping;
  unknown image-brush bounds now report unsupported. Retained dependencies remain
  registered in either mode so a later refill is visible to invalidation.
- Added source and paired adapter fixtures for known-empty, unavailable and
  zero-sized bounds, including retained dependency assertions. Extended the
  existing native host harness with real source DrawingGroup/DrawingImage/
  ImageBrush/DrawingVisual clear, unchanged-empty and refill transitions.

The source harness asserts actual group identity, dirty notifications, native
scene draw counts, stable empty reuse and restored scene bytes. It is wired into
the existing native host gate before window creation. Its public-API reflection
is diagnostic-only dual-assembly binding with a documented direct-reference exit
path; product code adds none.

## Source tracing decisions

`DocumentPageView.DuplicatePageVisual` still requires Windows RenderTargetBitmap.
The ordinary page invalidation subscription in `SinglePageViewer` is specifically
to the legacy FlowDocumentPaginator, not the portable paginator; the other traced
caller is printing suspension. This inspection did not establish an ordinary Showcase
resize/content-update dependency, so no offscreen renderer was opened in this
batch. The API remains an explicit broader gap, not completed support.

The Showcase page paragraphs do not set MinWidowLines/MinOrphanLines, whose actual
source defaults are zero. Their nonzero-policy rejection is therefore not itself
a demonstrated blocker for this application's default content. The separate
document-policy gap remains documented.

## Build-only evidence

Commands used the pinned root `./.dotnet/dotnet`, `build --no-restore -m:1
-v:quiet '-clp:ErrorsOnly;Summary'` with these projects:

- `src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj`:
  5 warnings, 0 errors, 6.06 seconds.
- `src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj`:
  final bridge build 116 warnings, 0 errors, 18.04 seconds.
- `src/ProGPU.Wpf.RealPresentationFrameworkHarness/ProGPU.Wpf.RealPresentationFrameworkHarness.csproj`:
  final rebuild 5 warnings, 0 errors, 56.90 seconds.

No tests, verifier workloads, apps, GPU work, VM work, benchmarks or CI polling
ran. Native/managed scene and pixel parity, actual Showcase/Toolkit execution,
package production, Windows admission and final required CI remain open. All
unrelated ProGPU native edits and performance-artifact deletions were preserved.

See the [shared ProGPU contract and primary-source record](../external/ProGPU/docs/native-mil-empty-drawing-images.md).
