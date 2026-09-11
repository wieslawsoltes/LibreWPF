# Source automatic anchor sizing

Acceptance: unchanged RealXamlCompilerHarness document, presented through
RealApplicationRunHarness. Its default Figure and Floater declarations require
source automatic width policy before placement and exclusion formatting.

`CreateAutoSizedAnchored` connects the source policy to ProGPU's native two-pass
width resolver. Automatic Figure and non-stretch Floater use FitContent; stretch
Floater uses Fill. Source `MbpInfo` supplies actual margin/border/padding, including
resolved automatic defaults. The first native result constrains original child
layout; measured content feeds the second native call. RequiresRemeasure creates
a new original-source layout at the resolved width and releases the initial one.
No glyph scaling or managed paragraph width scan is used. Failure disposes the
owned generation, and source document identity is validated before measurement.

Policy evidence is the existing FloaterParagraph automatic shrink condition and
AdjustDurAvailable margin/border/padding handling. Non-auto Figure reference
frames are deliberately not inferred from the available local width. This entry
point rejects non-auto widths; the existing direct child measurement remains.

The focused source fixture exercises default Figure, the Floater's actual effective
alignment, explicit Stretch and Left, retaining original child Paragraph identities and checking
outer insets independently from child content dimensions.

Validation: source build passed (0 errors, 1 existing warning, 42.21 seconds).
The first fixture incorrectly assumed the Floater's effective alignment was
Stretch; it reported an actual shrinking width of 480.5234375 DIPs. The corrected
fixture reads the real source property and also explicitly tests Stretch and Left.
Its rebuild passed (0 errors/warnings, 9.56 seconds), and the macOS ARM64 source
native-host smoke passed including both anchor generations and device recovery.
This is prepared-checkout evidence, not exact-head package/platform qualification.

Remaining: connect the collector and parent placement/exclusion production,
child drawing/input ownership, non-auto/reference-frame policies, empty/exhausted
anchor contracts, and final unchanged application/package/platform qualification.
Parent AnchoredBlock admission remains closed; this helper is not full delivery.
