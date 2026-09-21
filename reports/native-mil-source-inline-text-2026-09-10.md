# Source embedded text object connection

## Acceptance path

Showcase's inline Button depends on source TextEmbeddedObject support before
the FlowDocument paragraph source can admit its real child. This batch connects
that shared source line contract and exercises a real TextBlock/InlineUIContainer
Button through the existing native host harness. FlowDocument child ownership
and Figure/Floater exclusion layout remain separate unfinished connections.

## Implementation

PortableTextLine accepts fixed-size, single-source-symbol objects with desired
breaks on both sides. Source Format supplies finite width/height/baseline metrics;
the native provider owns line fitting, positioning and interaction. Every object
retains its original TextRun and source map entry. A source physical-face probe
supplies the non-ink style without font-shaping the object's U+FFFC.

Native measured lines supply source TextLine height/baseline. Glyph positions
subtract the actual native baseline once before GlyphRun export. Objects never
enter glyph id conversion or font lookup. Retained drawing order interleaves
source object callbacks with glyph runs; object ink comes from its own callback.

GetTextBounds retains line-height selection rectangles but also supplies the
actual object TextRunBounds, including source position and run identity.
TextBlock's existing ComplexLine arranger uses those bounds to retain and arrange
the actual child. Object bounds use keyed lookup, not repeated object-list scans.
Wrapped continuations retain the same immutable paragraph and source object list.

Variable-size or multi-symbol objects, non-default break policy, decorated objects,
non-baseline alignment and explicit fixed line-height combinations stay rejected.
No full FlowDocument, anchored, trimming, package or platform qualification follows
from this source connection. Source paragraph object admission remains closed.

## Verification scope

NativeMilSourceInlineObjectSmoke checks a real source Button, actual retained
visual parent, narrow/wide/narrow reflow, height mutation, native surrounding-text
export and source collection clearing/detachment. It runs in the native host
gate rather than replacing the unchanged application.
The first build exposed a C# local-name collision with existing glyph bounds;
the object layout variable was renamed without changing geometry.

The source harness build passes with zero errors and one warning. Final native
retention/host execution passes, including the real source Button with LTR/RTL
reflow, zero width, height changes, detachment and reattachment. Existing source
text/document/geometry and actual frame/device-recovery checks remain passing.
The corrected cleanup assertion follows TextBlock.ArrangeOverride's existing
proxy-collection clearing: the child may retain its disconnected proxy, but
TextBlock must have no visual children and that proxy must have no parent.
No production teardown was changed to satisfy the fixture.

After the complete source build, fixture-only rebuilds reuse those unchanged
project references and pass with zero warnings/errors. The existing compiled
bridge suite passes 1,764/1,764 against the updated tree with no skips; documentation
verification passes. Prepared-worktree logs are artifacts/source-inline-host-build.log,
artifacts/source-inline-fixture-build.log, artifacts/source-inline-native-host.log,
artifacts/source-inline-bridge-tests.log and artifacts/source-inline-docs.log.
These are local source-built checks, not exact-head package/platform qualification.
