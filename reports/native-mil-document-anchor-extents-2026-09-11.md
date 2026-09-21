# Document anchor extent and lifetime connection

Acceptance remains the unchanged RealXamlCompilerHarness document through
RealApplicationRunHarness. Placed child anchors must participate in document block
advancement and remain owned until the parent layout generation is retired.

`CreateWithAnchorFrames` snapshots explicit original-Paragraph reference arrays.
The existing document width pass precedes child measurement. Each requested
paragraph creates and retains its source anchor batch, places children through
ProGPU, and formats the original parent with the resulting exclusions and live
child ownership. Every request must target an actually formatted paragraph.

The positioned paragraph's supplied occupied extent includes both native text
extent and the returned native anchor right/bottom edges. The existing shared
native document arranger performs block placement and following-block advancement;
the source does not repair line positions or add a separate block composer.
The parent owns all child batches and releases them on failure or disposal.
Native paragraph fragments retain their local tops and original text positions.

The fixture uses real mixed anchors and a following source paragraph, verifies
that its native position clears the occupied anchor bottom, and checks that parent
disposal closes access to the owned child generation. This remains an explicit
reference-frame integration lane. Normal Create does not infer frames or admit
anchors yet: automatic source reference/convergence, child drawing and text-view
routing must connect before application admission. Package/platform/CI qualification
and remaining placement policies are still separate requirements.

Validation: prepared source build passed (0 errors, 1 existing warning, 31.60
seconds). The macOS ARM64 native-host smoke passed the following-block clearance
and child-disposal checks, paired source shaping and device recovery. This local
prepared-checkout evidence does not qualify exact-head packages or other platforms.
