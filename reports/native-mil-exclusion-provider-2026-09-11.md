# Native exclusion provider connection

Acceptance dependency: ShowcaseApp's Figure/Floater document needs actual native
fragment layout before source anchors can be admitted. WpfPortableTextFormatting
now implements the explicit IPortableExcludedTextFormatting capability, mapping
source-resolved exclusion rectangles and style/object metrics through the shared
native snapshot. Single-face cached contexts, isolated multi-face contexts and
retained physical render-font annotations keep their existing ownership.

Only excluded outputs implement IPortableExcludedTextParagraph. Their line tops
come from native fragment placements, never an ordinary prefix of fragment
heights. The separate double tops and native extents are retained. Inline-object
baselines, non-ink identity, source cluster mapping and line-local selection reuse
the existing adapter. Physical caret movement binds the native snapshot's own
generation and the request's paragraph direction. Ordinary and inline paragraph
contracts remain unchanged. No source anchor or SDK admission guard was removed.

## Verification and source alignment

ProGPU `586e52c7721f0957916159530a52e6d7e4ec1a3d` supplies the native snapshots,
navigation and neutral capability. Canonical LibreWinForms
`51cab2690a72b31dcea2e378931fd32c226dbc4e` selects that same producer.

The prepared source-built harness compiles with zero errors (two existing
warnings on the full build, one on the final incremental adapter build).
The native retention/host gate passes with the new provider cases: LTR/RTL
clearance, inline-object baseline/source identity, same-row fragments, line-local
selection, point hits, physical caret movement and independent source arrays.
Existing rich-document, child ownership, native input/geometry and device recovery
checks also pass. Logs are `artifacts/excluded-provider-host-build.log` and
`artifacts/excluded-provider-native-host.log` in the prepared WPF checkout.
The existing skip-build option was used only after that local build completed;
it is not a package or CI bypass. Staged native libraries came from the producer
worktree. These are local source-build results, not exact-head package provenance.

Figure/Floater still need real source anchor subtree measurement/placement,
exclusion-dependent source line consumption and original document positions.
Empty excluded rows and remaining fitting cases stay explicit. Full unchanged
ShowcaseApp/package startup and Windows/Linux/VM qualification remain open.

Fresh ProGPU browser CI at navigation head `ca339ea3` failed job 103067350750 at
stage `evidence-readback`, with no browser errors and the unchanged 120-second
deadline. Earlier browser success is not evidence that this new run passed.
The timing/queue/readback cause remains under investigation; no timeout, workload,
sample or pixel assertion was weakened. Keep all PRs unmerged until their required
CI and core application gates are satisfied.
