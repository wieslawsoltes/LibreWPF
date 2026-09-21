# Toolkit empty rectangle and next native input blocker

Current checkpoint: repeated TileBrush page admission is fixed; the next failure
is native scene compilation at AvalonDock auto-hide overlay (final section).

Latest follow-up: native input compilation is repaired; rendering now rejects
retained cache ownership/capacity preflight. See the final section below.

Acceptance application: ProGPU.Wpf.ToolkitApp. Action: focus its filter after
first native presentation and transient-surface quiescence. The actual source
DrawRectangle record contains (+infinity, +infinity, -infinity, -infinity), the
canonical WPF Rect.Empty. The native builder previously rejected it as width.

ProGPU now retains this static record and consumes it in the shared C++ compiler
as empty drawing, with resource validation and unchanged scopes/visual traversal.
LibreWPF's managed decoder applies the same exact sentinel before brush mapping
in both primitive and ordinary sink routes. Zero area is not empty; zero-width
pen commands still replay. Animated and other rectangle-bearing contracts remain
separate. See ProGPU docs/native-mil-empty-rectangle.md.

Both native providers build and all 19 native suites pass. The diagnostic bridge
build has one existing CS0067 warning; its tests build with 20 existing analyzer
warnings and no errors. All 201 compiler/decoder tests pass, including four new
cases across managed replay, both C++ providers, source point policy and other
visual owners. No test threshold, application action or native-input gate changed.

The unchanged Toolkit diagnostic now advances beyond rectangle translation but
fails recorded hit-index construction with UnsupportedCommand. Temporary native
return-location diagnostics identified the failure of add_recorded_hit_test_index
with source_geometry opacity policy, not append_visual. Those diagnostics were
removed from source immediately after reproduction. Next: locate the rejected
input primitive/scope and repair that shared native contract, then repeat the
same complete live gate. This is not Toolkit success or package qualification.

Artifacts remain under /Volumes/1TB-macOS/progpu-core-release.xtwndj:
toolkit-diagnostic-rectangle-details.log, toolkit-diagnostic-empty-rectangle-native.log,
and toolkit-diagnostic-unsupported-location.log. These runs used the documented
diagnostic source graph and explicit assembly/native overlays, never final packages.

The continuation source fixture was also executed with current source Core and
Interop, using the Interop additional-deps descriptor for this diagnostic graph:
20/22 pass, including the new changed-width/captured-provider regression. Two
existing hidden-only range assertions expose the older terminal-boundary special
case admitting all hidden ranges; distinguish actual newline caret bounds before
claiming the source suite passes. No assembly-loading failure counts as a test pass.

ProGPU Build 34811802371 finished successful at a8afeab6; new local continuation
and empty-rectangle changes require fresh CI. No downstream pins or merges advanced.

## Hidden selection versus terminal caret follow-up

The original terminal-caret fix admitted any hidden-only selection range. Narrowed
it to positive ranges intersecting the actual source newline, preserving native
caret X and retained line height. Hidden formatting edges still participate in
navigation but do not acquire selection rectangles. Added explicit terminal-box
assertions for both a hidden-only paragraph and a continued shaped line; existing
hidden-range assertions remain unchanged. Source Core builds with zero warnings;
the test build has six existing warnings. All 22 PortableTextLine tests now pass,
none skipped, with the current diagnostic source Core/Interop graph. The changed
continuation-width/captured-provider test is included in those 22.

## Singular image input fixed; retained cache preflight next

The rejected scope is a 19.9999-by-20 image rectangle under a zero linear affine
transform, not an unsupported clip. ProGPU now omits its noninvertible query
geometry while preserving scope/owner restoration, matching existing managed
image/point input policy. All 19 native suites and paired managed rectangle tests
pass, including rank-one, tiny invertible and mirrored transforms. See ProGPU
docs/native-source-rectangle-transform.md for exact provenance and limits.

The unchanged Toolkit diagnostic gets through native compilation and reaches
RenderScene, then fails semantic cache preflight with the existing owner-conflict
or bounded-pool diagnostic. Next trace cache_budget.add using real owner identity,
extent, shared status, content revision and effect status; do not increase limits
or disable caching without identifying which invariant failed. Log:
toolkit-diagnostic-singular-image-input.log. No Toolkit success, final package
qualification, dependency-pin advance or merge is claimed.

## Shared tile capture fixed; AvalonDock auto-hide next

Preflight recorded only two 20x20 pages (3,200 bytes). A repeated TileBrush's
identical owner/revision/extent was emitted twice without shared-page admission.
ProGPU now marks its already-normalized, source-revision-keyed tiled captures
shared. Per-paint opacity and composite mapping remain independent, and existing
owner/extent/revision/recursive-use/budget guards are unchanged. All 19 native
suites pass. A new GPU oracle covers four source kinds, four tile modes and one/
two half-opacity paints, checking every pixel plus one cold/zero warm capture
passes: 32 cases, 64 renders, passed on Metal. See ProGPU's
docs/native-mil-shared-tile-pages.md for provenance and platform limits.

The unchanged Toolkit diagnostic now completes actions through filter text,
popups, document/anchorable menus, editors/resources, wizard, child/message/window
controls, zoom/scroll/panels, data grid/collection, themes/options, document/editor
activation and keyboard navigation. Native scene compilation rejects the next
action, AvalonDock auto-hide overlay. Log: toolkit-diagnostic-shared-tile-pages.log.
Temporary preflight diagnostics were removed before that run. No application
deadline, input assertion or acceptance step was removed. The complete Toolkit
gate, final exact packages, downstream CI and merges are still open.
