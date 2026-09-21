# Source TextLine floating request connection

Acceptance remains the first Window measurement in RealApplicationRunHarness over
the unchanged RealXamlCompilerHarness MainWindow. Automatic anchored source
production remains guarded while the owned child placement path is connected.

PortableTextLine now accepts an explicit IPortableFloatingTextSource request after
collecting the actual hard segment. Its existing PortableTextSourceMap validates
and maps original hidden child ranges. The captured optional floating provider
receives one ordered event batch, existing styled face metrics/inline objects,
source empty-row metrics, paragraph origin and prior exclusions. Conflicting
excluded/floating requests, unbounded widths, missing capability and intrinsic
measurement requests fail explicitly.

Returned placements are checked for complete count and unchanged event positions.
Each is paired with its original source child start, not reverse-mapped from the
ambiguous shaping boundary: adjacent hidden children may share that boundary.
Wrapped TextLine continuations borrow the same immutable placement memory and
native paragraph. Parent fragment extents remain independent of floating occupied
extents. No glyph, fake space, source-side line fitter or Y repair was introduced.

The prepared RealApplicationRunHarness Release build succeeds in 99.19 seconds,
including source PresentationCore, PresentationFramework and the real WPF adapter.
It reports zero errors and two warnings outside the edited files (CS0067 in
WpfPortableDisplayMetricsSource and IDE0031 in PortableWindowActivationService).
The new floating request path is compiled, not yet runtime-exercised by the
document producer. Lower-level mapping/provider fixtures remain separate evidence.

The next source producer must select original anchors for each complete hard
segment, retain the measured child generation, consume native placements, and carry
earlier float boxes across LineBreak continuation. Advance from native parent text
bottom, not child occupied bottom; the existing exclusions decide later clearance.
Only then can the document layout enable automatic anchored child drawing/input.

Dependencies are aligned to ProGPU e0001f1d through LibreWinForms. Prepared build
submodules were clean before switching their detached heads; main-workspace dirty
submodules were not modified. Prepared source files are mirrored implementation
inputs, not exact-head packaged provenance. Source request compilation does not
qualify the application, platform input/pixels or package startup. All final gates
and explicitly deferred broader compatibility remain open.
