# Viewer fragment navigation

Acceptance application: the unchanged document application, with Up/Down beside
Figure/Floater content and across neighboring source blocks. This checkpoint
connects the scrolling viewer consumer; automatic anchor admission remains closed.

For a layout containing positioned paragraphs, the source text view now invokes
the retained TextLine native caret adapter for each physical vertical step. It
retains suggested X in document coordinates, maps source hit affinities, checks
that returned fragment indices stay in the original paragraph, and counts rows
rather than source fragments. Ordinary layouts keep their existing path.

At the native paragraph boundary it skips remaining same-top source fragments
and uses the existing source block/table navigation hierarchy. Entering another
row selects the retained span nearest suggested X, restricted to the selected
cell when tables are present. Original source pointers and block-control boundary
handling remain authoritative; no cloned text or source-local paragraph composer
is introduced.

Paginated consumers do not select this scrolling-view branch. Full viewer behavior,
including reverse movement, mixed table/excluded content and boundary affinity,
still needs runtime qualification after the automatic anchor source path connects.
Hard-segment origins and anchor child ownership remain implementation blockers;
the full application/package/platform gates remain open.

The prepared Release source build passed in 38.55 seconds with zero errors and
one existing IDE0031 warning. The macOS native-host regression smoke passed,
including existing ordinary table navigation and the source native caret adapter.
Those fixtures do not yet exercise the new positioned viewer branch end to end.
