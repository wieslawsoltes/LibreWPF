# Source fragment caret adapter

Acceptance path: the real document viewer beside Figure/Floater exclusions.
Action: Up/Down must move between physical rows, not successive source fragments
sharing a row. The current source viewer still indexes lines for vertical movement;
this checkpoint supplies its source TextLine adapter, not completed viewer wiring.

`PortableTextLine.TryMoveFragmentCaret` selects a retained native caret in its own
fragment using the actual mapped source offset and affinity, delegates movement
to `IPortableExcludedTextParagraph.MoveCaret`, and maps the result back through
the retained source map. Preferred X uses the same native origin as drawing and
hit testing. The result includes the fragment delta for the owning document layout.
Ordinary lines explicitly decline the capability. Invalid positions, missing
native carets and invalid returned indices fail rather than inventing a stop.

The source-built excluded-text fixture checks Down from each of two same-row
fragments and requires a later-row fragment and advancing original source offset.
The Release harness build passed in 53 seconds (zero errors, one existing IDE0031
warning). The macOS native MIL host smoke passed, including the new source check,
existing rich-document checks and same-window device recovery.

Remaining: consume this adapter in the viewer while preserving table/page and
paragraph boundary navigation, verify Up and boundary affinities, and connect
automatic anchor ownership and hard-segment origins. This focused host fixture
does not qualify the unchanged application or package/platform gates.
