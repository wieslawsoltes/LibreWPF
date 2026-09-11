# Owned source anchor placement and exclusions

Acceptance: the unchanged RealXamlCompilerHarness document through
RealApplicationRunHarness. Its measured sibling anchors need shared native
placement, followed by exclusion-aware parent text formatting.

The retained source anchor batch now captures its typed native sizing/placement
provider. `Place` accepts one explicit paragraph-local reference rectangle per
original anchor, validates the source generation and supported source policy,
then submits the measured outer sizes in one native batch. Figure horizontal
anchors and Floater alignment select the source horizontal policy; Figure delay
permission is retained. Non-default wrap sides, fixed Figure height and offsets
fail explicitly instead of being silently ignored.

Returned child placements borrow the batch's actual measured layouts. Content
origins include source margin/border/padding exactly once. The same native outer
rectangles become immutable text exclusion requests, distinct from child input
geometry. No managed collision loop or Y repair is added. Source conversion is
O(A) storage/work for A anchors; native bounded placement remains the existing
shared algorithm. Failure publishes no placed batch and keeps measured children.

The caller must resolve real source reference frames before this operation:
Figure ParagraphTop is not a Floater anchor-line position. It must retain the
owning batch while borrowing placements, and invalidate drawing/input with the
parent layout generation. This API does not discover frames or extend frame
height using a viewport guess.

The source fixture places two real measured sibling anchors with distinct
reference tops, checks ordered child identity, half-open nonoverlap and inset
origins, then passes the produced exclusions into native parent shaping. Child
text stays separate and the parent preserves every original document symbol.

Validation: prepared source harness build passed (0 errors, 1 existing warning,
34.65 seconds). The macOS ARM64 native-host smoke passed mixed-anchor placement,
derived-exclusion parent shaping, original source ownership and device recovery.
Prepared-checkout evidence is not exact-head package or Windows/Linux qualification.

Still required: automatic reference discovery/convergence in normal document
layout, parent extent consumption, child drawing and text-view routing, remaining
source placement policies and exact-head application/package/platform gates.
Normal parent AnchoredBlock admission remains closed until those connect.
