# MVP full-ellipse native input — 2026-09-09

## Acceptance dependency

Application/action: open the existing full package MVP's Shapes tab and preserve
selection and native input admission for `MvpShapeEllipse`, including content
updates. The shape has Width/Height 54, StrokeThickness 3 and Canvas offset
(104,16). Source `Ellipse.ArrangeOverride` produces the inset 51x51 defining
ellipse; `Ellipse.OnRender` emits DrawGeometry with EllipseGeometry, not a direct
DrawEllipse shortcut. This is a source trace, not a reproduced runtime failure.

Blocking source path: ProGPU native MIL's primitive-geometry stroke lowering
emits `PROGPU_NATIVE_GEOMETRY_ARC` with positive axis-aligned radii and a full
positive sweep. The native hit-index producer accepted only lines in that command
family and rejected the ordinary ellipse stroke. This is a core application
dependency, not general arc/Direct2D API expansion.

## Implemented connection

ProGPU `4a30bb59` connects canonical full elliptical arc strokes to its existing
ellipse hit primitive. Native analytic ellipse draws share the same encoder.
Actual owner, fill/stroke separation, local pen width, inverse affine placement,
intrinsic broad-phase bounds and source clip handling remain intact. Partial,
skew-basis and device-width arc contracts remain explicit unsupported inputs.

Managed source traversal already uses the existing ellipse encoder, so its
product path is unchanged; paired managed regressions cover the actual MVP
shape/update/removal and noncircular affine payload/bounds contract. Native
fixtures cover canonical MIL plus arc/analytic equivalence and negative inputs.
There is no new shader, wire record, raster implementation, CPU readback or
source-local geometry replacement. WPF and the bridge already export the
correct source geometry and require no production edit for this connection.

The previous drawing-mask managed fixture also now expects zero extra clip
segments for an axis-aligned source clip: that encoder retains the exact clip in
primitive bounds. The bounds assertions still require clipped coverage. This
correction came from source inspection, not test execution or weaker semantics.

See [ProGPU's paired applicability, design and research record](../external/ProGPU/docs/native-mil-ellipse-input.md).

## Build and delivery record

SDK 10.0.201 managed `dotnet build src/ProGPU.Tests/ProGPU.Tests.csproj -c Release
--nologo -v quiet` succeeded with 0 warnings, 0 errors in 37.43 seconds. Regression
fixtures are compiled, not executed.

Clean immutable checkout `4a30bb59` built successfully for macOS ARM64 and x64,
17 incremental compile/link steps each. Both configured wgpu-native/Dawn shared
libraries and native MIL/test/sample targets completed. These are AppleClang
header-mode builds, not C++20 module execution or Windows/Linux qualification.
The implementation is delivered through
[ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139), with this report
and submodule update in [LibreWPF PR #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).

The native implementation changed only the builder producer, not MIL command
decoding; the existing MIL coverage source digest therefore remains unchanged.
Clean native checkout builds exclude the four unrelated scene edits and unrelated
performance-artifact deletions in the working ProGPU tree. Neither is modified
or staged by this batch.

No source verifier, test, application/VM/GPU workload, image comparison, benchmark
or CI qualification was run. No platform payload was staged or package refreshed.
Complete module/provider/platform builds, package-mode acceptance, Windows native
comparisons, lifetime/performance and exact-head required CI remain final gates.

## Remaining finish criteria

This closes the identified MVP ellipse admission branch in implementation, not
runtime selection fidelity or the full application queue. Continue tracing
required application actions and their actual unsupported paths; do not open
general arc families merely because the native producer still lists them.
Feature freeze and final qualification remain pending.
