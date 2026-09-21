# Automatic native floating document connection

The unchanged RealApplicationRunHarness now passes its former first-window
anchored-block measurement failure. Its next observed failure is later in
ActivationRecorder.ValidatePortableAccessKeyActivation: Alt+A reports Handled=false.
This is progress through actual application startup, not a claim of passing the
application, rendering/input parity or package qualification.

Ordinary bottomless document layout now discovers original paragraph anchors,
measures their actual child layouts through the existing native two-pass sizing,
and requires the optional floating text/positioned document providers. Explicit
fixed-frame anchor fixtures retain their separate placement contract.

Each hard segment publishes only its complete hidden child ranges. PortableTextLine
maps them through the shared source map; native paragraph rows place the children.
Returned original source starts bind to the same measured child generation before
publishing the retained drawing/input placement batch. Following document blocks
consume max(parent text bottom, child occupied bottom), not a sum of fragment rows.

Hard-line continuation advances from the native parent text bottom and carries
prior float boxes as exclusions. Wrapped continuations reuse the same paragraph
and publish its children only at the final fragment. Real embedded child controls
join the existing retained host collection. Failure disposes the newly owned child
generation and parent layout through existing cleanup. Ordinary paragraphs do not
allocate a floating exclusion list.

Bottomless Figure uses the native floater delay policy, distinct from explicit
fixed-anchor CanDelayPlacement. Non-auto width, fixed height, offsets, wrap sides,
and unsupported source cases still reject. Finite-page pagination explicitly
rejects anchored layouts before fragmentation; bottomless placement is not finite
page ownership. Empty hard segments without events still require an explicit
native empty-row contract and can fail; no fake glyph or successful omission.

The prepared source application build passes with zero errors and one existing
IDE0031 warning outside edited files. The actual run fails at the access-key check
above after measurement succeeds. New focused source regressions exercise automatic
Figure/Floater ownership, source-row delay, child controls, occupied following-block
extent and hard-segment carry-over. Final regression results are recorded below.

The final prepared harness rebuild passes with zero warnings/errors. The native
MIL host script passes both retention and host/device-recovery lanes, including
the new automatic Figure/Floater tests and existing rich-document, inline control,
excluded-line, geometry selection and text contracts. The first new fixture run
hit a test-only reflection helper that read properties but not generated position
fields; the helper now reads both and the unchanged coordinate assertions pass.
This is macOS arm64 local source/native evidence, not Windows/Linux qualification.

The original child measurement, C++ floating fitter, source map and drawing/input
ownership are reused; no foreign implementation or source-local composer was added.
Both renderers share this path. Full application/package/platform/CI gates and
deferred broader compatibility remain open.
