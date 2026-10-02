# Original Display source bridge

The internal `WpfSourceDisplayTextParagraph` adapter adopts an explicitly created
native source-double paragraph and prepares its resource from that exact owner.
It forwards native line-scoped double queries, preserves original UTF-16 text and
styles through retained references and double-width reflow, and uses native double
source-run validation before binding. Its binding remains the concrete native MIL
type across `Retain`, carrying original double identity separately from the raster
projection. No ordinary formatter advertises Display or chooses an unproved
em/advance/interpreter policy. Optional intrinsic widths are copied from the
producer's whole-paragraph double measurement; they are never the current formatted
line width. Zero-content-width admission remains unavailable here.

Seven adapter identity/schema/routing controls are authored. They and the backend APIs need
the pending coherent ProGPU source branch; the gitlink deliberately remains the
qualified old pin until that producer is published. No local dependency-graph,
native, GPU or desktop qualification is claimed.

Native MIL batches now select the explicit source-resource import only when an
actual retained producer resource carries that extension. Mixed raw/source owners,
original binding indices and commands cross together in one atomic provider call;
missing source capability is not retried through raw import. Ordinary raw batches
retain their existing route. This source wiring and its hosted selector are
committed pending the coherent published ProGPU dependency; the existing gitlink
is deliberately unchanged, so it does not yet provide these new APIs.

The portable `TextLine` source route now selects `IPortableDisplayTextFormatting`
only for an actual Display formatter. Explicit hinted formatting and font/device
metrics alone do not admit that route. The current WPF provider deliberately does
not implement the new capability: issue #184 remains open pending the complete
native Display producer, provider bridge and original application qualification.

The source forwards its complete hard paragraph and original double em/DPI,
line/style dimensions, content width and indent. It retains physical fonts,
UTF-16/style/feature/digit/bidi identity, wrapping and intrinsic measurement intent.
It never calls Ideal as a Display fallback. Unsupported empty rows, tabs, objects,
exclusions, floating content, zero available width and actual collapse fail
explicitly rather than discard policy or format an Ideal collapsing symbol.

Separate double native views supply glyph advances, writer frames and intrinsic
widths. Source hit input and caret output retain doubles; selection and logical
boundaries use the same original generation. Source code does not divide by DPI,
round an advance, compute a nominal Display offset or reshape a prefix. The new
Display glyph binding validates exact double source metrics/frame before publishing
ink/ownership, while retaining the existing typed renderer lease. The current
render device-scale/em projection still requires exact float representability;
it must reject, not relabel an original double. This is not a float-only source
geometry definition: seven device pixels at DPI 1.5 remains a double advance.

Every independent paragraph Retain validates original text, style/em and DPI
identity. Source lines and cloned breaks keep their existing independent lifetime
and retryable cleanup. A width change calls `ReflowDisplay` on the captured whole
paragraph, captures its returned producer before validation, and checks identity
before publishing a continuation. Removing the provider or mutating public
TextLine.PixelsPerDip cannot change that captured generation.

Thirteen actual source formatter/line/GlyphRun regression cases use recorded typed
producers, not fake native resources. They cover missing capability, exact request
identity, double geometry, intrinsic widths, bad initial/Retain/Reflow identity,
continuations after provider removal, device change, unsupported policies and
atomic binding publication. The existing hosted source-core build runs the new
class with a 60-second timeout and fail-skips; no existing gate is weakened.

## Dependency and validation status

This source commit is authored against the additive ProGPU contract checkpoint
`0e935c561` on the corrected MIL foundation `d947a1293`. Per integration policy,
the WPF gitlink remains unchanged during authoring. Therefore the current pinned
48 dependency cannot compile this pending source change. The native worker will
integrate that contract and the complete producer on one ProGPU Display branch;
WPF must pin its exact published head in a separate commit before PR publication.
Native runtime staging still requires the whole exact producer Build to succeed.

No local source graph, native renderer, GPU, VM or runtime staging was run. Tests
are authored for the hosted actual-source lane, not reported as executed. The
recorded contract fixtures do not qualify native numeric parity or unchanged
AvalonDock menu/tab/title rendering.

Postcommit checks for source implementation `56c01783d`:

- `git diff e9beec518 --check` and `bash -n eng/progpu-wpf-display-source.sh` passed.
- SDK 10.0.200 Roslyn parsed all seven changed product/fixture C# files across
  the paired source and neutral trees with zero syntax errors. Each parser
  process had a 15-second timeout. This was parsing only, not type compilation.
- XML project inclusion, all 13 source case declarations, exact hosted class,
  minimum count/fail-skips/60-second timeout, and prerequisite build order passed
  offline checks. No existing source selector or deadline was replaced.
- The production provider still does not advertise Display; the original
  `48a49afeb993214c0c40c6908e9896ef0b5dec97` gitlink is unchanged.

Product compilation and all authored test execution remain pending the coherent
published dependency and hosted exact-source build.
