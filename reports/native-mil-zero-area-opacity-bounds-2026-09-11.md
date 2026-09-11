# Exact zero-area opacity bounds

The external application's native render failure was reproduced with a diagnostic
bridge assembly. The rejected source is a Rectangle with descendant bounds
`(0, 0, 0, 4)` and opacity-only isolation. The source advertised those finite,
zero-width bounds as unavailable; the compiler independently required positive
extent even for its existing non-isolated opacity path.

Visual's typed descriptor now admits finite nonnegative extents while preserving
the original rectangle and IsEmpty distinction. The compiler admits zero area
only for the opacity-only path, retaining native visual alpha, children and source
input scopes without allocating an isolation texture. Effects, masks, caches and
visual-brush isolation retain their stricter requirements. Missing, negative and
nonfinite bounds remain failures; there is no layout-rectangle substitute.

The existing native/managed input-scope regression now covers both zero axes,
growth/clear transitions and malformed metadata. Source compilation is running;
regression execution and external application revalidation are pending.

Diagnostic reproduction replaced only the generated external application's bridge
DLL, preserving its original at
`artifacts/native-exact-b54db165.dBPf7B/external-ProGPU.Wpf-before-bounds-diagnostic.dll`.
That modified output is diagnostic, not qualified package evidence; a clean
package rebuild remains required.

Validation: source/harness build passed (two warnings, zero errors, 1m50s).
The focused four opacity/input-scope cases pass across WgpuNative and Dawn with
no skips (68ms). Tests used explicit build plus VSTest, as the repository's
default dotnet-test runner rejects this VSTest project. Test build emitted 116
warnings and no errors; no warnings were disabled.

The diagnostic external app with rebuilt PresentationCore and bridge gets past
the exact zero-width bounds rejection. It next fails native semantic scene
compilation for retained target 4509. That later failure remains undiagnosed and
must be resolved before claiming application closure. The original generated
PresentationCore is preserved at
`artifacts/native-exact-b54db165.dBPf7B/external-PresentationCore-before-zero-bounds.dll`.
