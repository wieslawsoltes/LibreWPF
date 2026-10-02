# Original Display source bridge

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
