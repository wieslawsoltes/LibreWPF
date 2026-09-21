# Retained fragment point selection

Acceptance application: the source-built document viewer used by the unchanged
RealXamlCompilerHarness through RealApplicationRunHarness. User action: click text
on either side of an anchored exclusion, including after scrolling.

The existing Y binary search selected the last source fragment sharing a row.
It then rejected a non-snapping click in an earlier fragment, or snapped the click
into the wrong fragment. Both the plain line path and table/object item path had
this assumption.

The viewer now selects the closest retained horizontal text span within the
selected native row. It reads the same native positions and TextLine.Start/width
used for drawing, preserves the existing cell/page range, and does not assume X
increases in source order. The ordinary non-positioned path is unchanged. The
row scan allocates no secondary layout or geometry index.

This change does not connect native vertical caret movement, hard-segment origins,
automatic Figure/Floater admission or anchor child ownership. Those remain required
before the unchanged application and package/platform gates can qualify delivery.
The prepared source-built RealPresentationFrameworkHarness Release build passed
in 43.87 seconds with zero errors and one existing IDE0031 warning. `git diff
--check` passed. This is compilation evidence, not viewer behavioral qualification;
same-row click fixtures and the full application remain required.
