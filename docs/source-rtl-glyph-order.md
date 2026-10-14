# Source RTL glyph occurrence order

`PortableTextLine` already orders right-to-left clusters by their original source
position when publishing a WPF `GlyphRun`. It previously retained left-to-right
visual occurrence order inside each cluster. A base glyph and its marks could
therefore appear in the opposite order from the original source run, with the
advance attached to a later occurrence.

The source projection now traverses each odd-level cluster in reverse retained
occurrence order. It does not sort glyph IDs, change shaping, infer positions from
IDs, or mutate the native paragraph. The same selected indices feed IDs, advances,
positions, cluster maps and hinted resource binding. Run grouping retains the full
embedding level; the public run still publishes direction zero or one. Even levels
keep their original order. The existing sort has the same O(G log G) complexity
and uses the existing index array without additional allocation.

The original Windows ARM64/x64 source and midpoint captures in
[ProGPU Build 37011550318](https://github.com/wieslawsoltes/ProGPU/actions/runs/37011550318)
provide the independent expected glyph sequences. A read-only audit of all 960
whole-run observations at qualified producer `0856f13acd6689116d71ded64132551c9476a460`
retained the raw glyph/run/descriptor identities and original cluster-coverage
checks. Directional traversal corrects all 240 differing odd-direction source run
sequences; the other 1,768 sequences stay unchanged. Of the corrected sequences,
216 have otherwise aligned source domains and 24 retain separate line-fitting
mismatches. Original diagnostic outputs and comparators remain retained. This
sequence audit does not reclassify those unmatched domains as qualified output.

The regression exercises the actual source formatter and GlyphRun export at full
levels 0 through 3, with nonmonotonic IDs, multiple occurrences in one cluster,
zero-advance marks and a separate adjacent cluster. It checks ID/advance/position
pairing, cluster maps, shared drawing/export identity and the original native-font
annotation. These authored adapter controls are separate from native shaping,
original font geometry, desktop pixels and Display admission.

The new regression failed against the original source adapter with glyph IDs
`3, 5, 2, 7` instead of `2, 5, 3, 7`. After the correction it passes, along with
all 124 source text and Display bridge controls, with zero skips. The
real source project builds with zero errors and its 17 existing warnings. The
original source gate includes this new test by raising its minimum count from
110 to 111; its 60-second bound and failure/skip policy are unchanged. Hosted
whole-Build validation remains required before integration.

[Microsoft's GlyphRun contract](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/introduction-to-the-glyphrun-object-and-glyphs-element)
defines direction and per-glyph placement. The
[HarfBuzz buffer contract](https://harfbuzz.github.io/harfbuzz-hb-buffer.html)
keeps shaped glyph occurrences distinct from character clusters. These inform
the interface boundary; the observed Microsoft runs provide its source oracle.
No foreign implementation was copied. Both managed and native rendering consume
the same source GlyphRun selection; their rasterization and GPU policies are
unchanged. Full Display frames, mixed-level mapping, caret/pixel comparisons and
application qualification remain tracked by LibreWPF #184.
