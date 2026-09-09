# MVP rectangular brush-fill input — 2026-09-09

## Acceptance path

Application/action: the full package MVP's resource tab contains
`MvpDrawingImageBrushBorder`, a 36x36 Border with a DrawingImage-backed ImageBrush.
Selection belongs to that actual filled rectangle, independently of the brush's
internal drawing, viewport, mapping or opacity. Source WPF's point and geometry
drawing-context walkers test the geometry for a non-null fill brush, without
sampling the brush. This is a source-backed finding, not an executed app failure.

The preceding Expander/ScrollBar template trace found glyph-based chevrons on the
MVP's Fluent theme, not an open-path arrow requiring new stroke support. This
batch instead addresses the concrete image-brush branch in the same full app.

## Implementation

ProGPU `a7dbc894` adds source rectangle ownership around native tile-brush replay.
The existing builder logical-rectangle mechanism captures geometry using source
placement and inherited clips, then excludes isolated/mapped/repeated brush
contents from the input index. The separate pen is outside the scope. Raster
brush resources and algorithms are retained. `2b2c20c5` adds documentation only.

The managed LibreWPF replay adapter publishes a typed source-rectangle scope
through both direct and retained sinks. Rectangle DTOs and geometry accepted by
the existing exact rectangle clip readers use the same entry; arbitrary path
bounds do not. The scope reuses ProGPU's retained command/index machinery and
closes through the normal sink Pop, including finally cleanup. Existing image
scope calls share its implementation. No source-local renderer or reflection.

Native regressions extend the existing brush fixture to opt into complete input
for bitmap/DrawingImage/DrawingBrush/VisualBrush rectangles, zero opacity,
remapped/rotated viewports, empty vector content, repeated tiles and separate pens.
The flag defaults off for existing fixtures. Managed product fixtures exercise
immediate/geometry rectangles with sparse/empty/missing drawing content, actual
outer clipping, separate pens and following draws. A retained fixture verifies
owner preservation and exclusion of brush-internal draws during typed traversal.

See [ProGPU's design, research and paired applicability record](../external/ProGPU/docs/native-mil-rectangle-brush-input.md).

## Build evidence

- Final SDK 11 preview root `dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj
  -c Release --nologo -v quiet`: 116 existing warnings, 0 errors, 31.62 seconds.
  Earlier intermediate builds also succeeded (29.60 seconds, 116 warnings;
  retained-fixture incremental pass 11.82 seconds, 20 warnings).
- Clean immutable native checkout `a7dbc894`: complete configured macOS ARM64
  and x64 CMake builds each completed all 12 incremental compile/link steps.
  Both wgpu-native/Dawn libraries and affected native test/sample targets built.
  These are AppleClang header-mode builds; module and Windows/Linux qualification
  remain mandatory. The following documentation-only commit changes no binaries.
- MIL coverage ledger regenerated after the source edit. No verifier/test,
  app/VM/GPU workload, image comparison, benchmark or CI qualification executed.
  Automatic CI remains enabled. No package or staged payload refresh occurred.
- The four unrelated ProGPU native scene edits and performance-artifact deletions
  were preserved and excluded from commits and clean native builds.

Implementation is submitted through [ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139)
and the paired bridge changes through [LibreWPF PR #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).

## Remaining work

This closes the identified MVP rectangular brush-fill input connection in code,
not runtime parity, final package consumption or the application queue. Other
geometry representations/local-transform combinations, nonrectangular brush
fills, unsupported clip combinations and BitmapCacheBrush/required picture
contracts remain explicit work. Only concrete acceptance dependencies belong
on the core implementation path. Feature freeze and final qualification remain
pending; neither PR is declared merge-ready from compilation.
