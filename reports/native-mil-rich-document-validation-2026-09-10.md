# Native rich-document editor connection

## Acceptance path and ownership

The actual source RichTextBox now uses FlowDocumentView in both renderer modes.
The shared view selects its portable formatter and ITextView from frozen media
ownership before accessing PTS, including on Windows. Its existing ProGPU C++
document-flow provider resolves widths and block placement; the original WPF
paragraph source and native text provider format the retained TextLines.

This removes the portable editor's linear TextBoxView substitution. Paragraphs,
sections and lists retain their document structure, margins, markers and original
TextContainer/TextPointer ownership. TextEditor attaches to the same live ITextView
used by source drawing. Selection, edit, undo and document replacement retain the
source lifecycle; there is no cloned document or second placement/composition
algorithm. The existing linear TextBoxView decoration tests remain applicable to
that consumer and do not stand in for rich-document layout.

## Empty source opacity

The real ScrollViewer template exposed an empty ScrollBar with nonidentity opacity.
Visual previously reported Rect.Empty as unavailable bounds. It now publishes the
authoritative empty descriptor. Native MIL keeps such ordinary opacity visuals on
ProGPU's existing uniform-alpha traversal, without manufacturing isolation bounds.
The visual, actual alpha, source point scopes, owners and descendants are retained.
Missing metadata and zero-sized rectangles remain rejected by this path. Effects,
caches and spatial masks retain their positive-area allocation requirements.

The implementation-specific native adapter correction uses the existing ProGPU
C++ uniform-opacity/source-geometry contract; no native ABI change or alternate
paragraph composer is needed. Paired managed input tests exercise the same source
scope semantics. No performance claim follows from removing this rejected empty
allocation; no compute-heavy algorithm is added.

## Validation and remaining work

The complete source-built native host gate passes, including the new real-template
rich editor fixture and existing same-window device recovery. The editor fixture
checks paragraph/section/list placement and markers, original text-view ownership,
source point positions, native glyph export and complete native hit-index scene
compilation, SelectAll, replacement, undo and detached-view invalidation.

The unchanged full Application.Run harness builds, then fails in
PortableFlowDocumentLayout.AddBlock with: "Portable document tables and embedded
blocks require their native layout contracts." No document nodes or application
assertions were removed. Table/cell layout, Figure/Floater, inline/block controls,
remaining text contracts and final package/platform qualification stay open.

Evidence in the validation worktree:

- artifacts/native-host-rich-document-final.log
- artifacts/application-run-rich-document-build.log
- artifacts/application-run-rich-document-tests.log
- artifacts/wpf-rich-document-bridge-final-build.log
- artifacts/wpf-rich-document-bridge-final-tests.log

The final Release bridge suite passes **1,761/1,761**, zero skips (20 analyzer
warnings, no build errors). Empty-opacity input compilation and metadata updates
pass with both wgpu-native and Dawn, paired with managed retained input. An initial
test invocation hit the SDK's MTP/VSTest selection mismatch; the existing VSTest
assembly was built and run directly, without changing the repository runner policy.
These source tests do not qualify package-mode applications or permit merging with incomplete
acceptance paths or pending required CI.
