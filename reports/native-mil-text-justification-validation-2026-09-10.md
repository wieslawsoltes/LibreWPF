# Source native text justification validation

## Acceptance action and change

The real Application.Run harness exercises rich-editor AlignJustify. Its custom
recording host now initializes the existing lazy ProGPU native media services
before loading source WPF or constructing media. This supplies the same typed
geometry/text/document defaults as the native host; it creates no device/window
and preserves explicit provider overrides.

ProGPU commit 2bf43d76c628060f9a919f107c58ebe5ed50130e implements source-cluster-aware
word-space expansion in the shared C++ paragraph. PortableTextLine forwards Justify
instead of rejecting it or silently sending Left. All other unsupported-property
guards remain, with diagnostic property values. No WPF-local spacing algorithm,
empty-text fallback or Windows-MIL renderer fallback is introduced.

## Evidence

- Source Application.Run harness builds successfully and progresses past the
  previous justification rejection. It now fails explicitly at portable rich-text
  decoration scopes; the application gate remains failing.
- The complete source-built native MIL host gate passes, including device recovery,
  existing text/collapse/geometry checks and a new styled/RTL justification fixture.
- The new fixture checks actual wrapped width, retained font/cluster/bidi identity,
  unchanged final-line geometry, expanded-space selection and caret distances,
  source point hits and combining-cluster logical navigation.
- ProGPU's complete local native suite passes 20/20, including uniform/styled,
  LTR/RTL, tab, final/hard-line and cluster-classification regressions.
- The complete Release bridge suite passes 1,755/1,755 with no skips after
  supplying the native library path and updating the recording-host source
  graph assertion to require its private media-provider reference.

Validation-worktree logs: artifacts/application-run-justification-build.log,
artifacts/application-run-justification-tests.log and artifacts/native-host-justification.log.
Bridge evidence is artifacts/wpf-justification-bridge-confirmed-build.log and
artifacts/wpf-justification-bridge-confirmed-tests.log (20 analyzer warnings, no errors).
These are source-built local results, not final exact-head package evidence.

## Remaining delivery work

The next application blocker is TextBoxLine.ValidateRichElement's decoration-scope
rejection. Preserve the real source structural/run semantics when connecting it;
do not delete the guard without implementing its contract. Script-specific native
justification insertion/inter-character policies remain separate missing parity.
Windows/Linux qualification, exact-head package application gates and green PR CI
are still required. ProGPU e7a6b89e CI additionally reported browser timeout and a
Windows per-point-path-guideline benchmark process failure; no threshold or gate
has been weakened to admit either.
