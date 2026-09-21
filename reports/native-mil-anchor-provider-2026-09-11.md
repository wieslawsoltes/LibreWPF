# Anchored document provider connection

Acceptance remains first presentation of the unchanged RealXamlCompilerHarness
document through RealApplicationRunHarness. This checkpoint connects the typed
provider needed by that source path, not Figure/Floater source admission.

ProGPU `08ec6501` exposes `IPortableAnchoredDocumentFlow` with sequential native
width/placement records and uint-backed width/alignment enums. LibreWPF's existing
`WpfPortableDocumentFlow` implements it through zero-copy span casts into the
shared C++ batches. No reflection, source child copy, alternate layout algorithm,
renderer fallback or implicit ordinary-block admission is added to the adapter.

The focused fixture compares every public field offset and size against generated
native records. It exercises measured fit-content width, chained source-order
placement and unchanged results after a later invalid item. All four document-flow
tests passed under VSTest on macOS ARM64 (zero skips). The Release bridge test
build passed with zero errors and 117 existing graph/analyzer warnings.
Native libraries were rebuilt in the prepared ProGPU worktree in the preceding
transport batch; these are local project-reference checks, not package provenance
or full source-built application/runtime qualification.

The selected graph advances to ProGPU `08ec6501428ad0757a25963623d31662c374d452`
and LibreWinForms `52ac7b24fcda2f26d734bb295ed26a61b6143c8f`, whose ProGPU pin
matches. A mistargeted WinForms feature branch was created during publication,
then removed with an exact-head lease after publishing the same commit on PR #29's
existing integration branch. No commit or existing user branch was lost.

Required next work: actual anchored source subtree measurement, reference/offset
and wrap-side policy, exclusion-aware source TextLines, retained child drawing and
original TextPointer/selection/caret ownership. `RequireInline` still rejects
AnchoredBlock; do not remove that admission guard merely because the provider
now exposes native primitives. Empty anchor and remaining native-fit contracts,
full application/package/platform testing and CI/merge qualification remain open.
