# Source floating text provider connection

Acceptance is unchanged: RealApplicationRunHarness opens the existing
RealXamlCompilerHarness MainWindow. Its first-measure anchored-block rejection
requires source events and native floating placement, not fixed-point source Y
repair. This batch connects the optional neutral provider to the existing
WpfPortableTextFormatting styled/inline/native snapshot path.

`FormatFloating` maps typed alignments, measured child sizes, explicit origin and
empty-row metrics. Native snapshots retain source UTF-16 events and placements;
the adapter publishes owned neutral output with original positions and physical
row indices. Existing fragment text metrics, caret/selection, physical fonts and
inline-object identities remain shared. Occupied extents include floating child
boxes separately from parent content extents. Requested intrinsic widths reject
explicitly until the corresponding native floating measurement contract exists.

Evidence: a linked-source probe compiled the actual adapter against prepared
ProGPU 654115fb interop/native sources, Release, zero warnings/errors. Native
execution passed same-row sibling and terminal events, retained source identity
after caller mutation, inline interaction, parent/child extent separation, the
native anchor-only row and explicit intrinsic measurement rejection. Probe project
and executable are local artifacts under ProGPU artifacts/floating-provider.9BYkbb;
this is not a source-WPF build, package or application qualification. The preceding
retained snapshot has 11 passing focused tests and passing native consumer checks.

Both renderer modes use the same text provider. No source-local composer or
foreign implementation was added. Dependency pins move together through ProGPU,
LibreWinForms, then LibreWPF; physical user-owned dirty submodules are untouched.

Remaining critical path: map original hidden document edges into floating events,
connect automatic child placement to the retained drawing/input owner, preserve
hard-segment origin and exclusion continuation, then run the unchanged application.
Automatic AnchoredBlock rejection remains until this source path connects. All
package/platform/CI gates and explicitly deferred broader compatibility remain.
