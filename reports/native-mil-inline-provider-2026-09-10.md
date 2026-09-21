# Native inline paragraph provider

## Acceptance path and implementation

Showcase's real InlineActionContainer/DocumentInlineButton requires a source-owned
measured text object, not generated spaces or a font glyph. This batch connects
the neutral provider to ProGPU's existing retained C++ inline snapshot. Source
TextEmbeddedObject, actual child visual/editing lifetime and anchored Figure/Floater
admission remain separate unfinished connections.

WpfPortableTextFormatting implements IPortableInlineTextFormatting explicitly.
It validates the explicit style/metric count, maps neutral records once per
request and calls NativeTextParagraphSnapshot.CreateWithInlineObjects. Cached
single-face contexts and temporary multi-face contexts retain their existing
ownership; returned native data and neutral placements are owned snapshots.
The native paragraph remains responsible for shaping, line fitting and interaction.

Only measured outputs implement IPortableInlineTextParagraph. Their line tops
use the same double height prefix as native interaction, with separate baseline
offsets; original positioned glyphs are not repacked. Object sentinels are marked
IsInlineObject and bypass font lookup validation, while ordinary glyphs still
require actual retained render faces. Selection remains line-local.

Ordinary formatting keeps its existing output convention. Measured collapse
fails explicitly until a source sign-metric contract exists. No Windows SDK
admission, renderer fallback or source inline rejection is removed.

## Dependency and verification record

ProGPU e574a911a6d89d562276b1ca4bb08fadf62cc184 supplies the neutral capability,
measured interaction and owned inline snapshot. LibreWinForms
82d595d3dc5c84d280557cf150fe1dd382229d6f pins that same producer.
The neutral project builds with zero warnings/errors and its two focused
compatibility/capability tests pass.

NativeMilInlineTextSmoke is part of the existing native host gate. It exercises
LTR/RTL, single/multiple physical faces, tall-object wrapping, per-line tops and
baseline offsets, source clusters, immutable placements, line-local selection,
caret distances, point hits and explicit measured-collapse rejection.
Compilation initially exposed a generated struct constructor mismatch; the
adapter now initializes the generated metric fields directly.

The final source harness builds with zero errors and two existing warnings.
The complete local native retention/host gate passes, including the new four-case
inline provider fixture, existing collapse/justification, source rich-document
and geometry checks, actual frame presentation and device recovery. Logs in the
prepared worktree are artifacts/inline-provider-host-build.log and
artifacts/inline-provider-native-host.log. Documentation verification passes.
The aligned Release bridge suite passes 1,764/1,764 with no skips. Its build
reports zero errors and 116 warnings across the dependency graph; see
artifacts/inline-provider-bridge-build.log and artifacts/inline-provider-bridge-tests.log.

The full unchanged application, package-mode startup, Windows/Linux execution
and exact-head CI remain unqualified. ProGPU's SVG checksum failure also remains
open. This provider connection is not completion of the source control path.
