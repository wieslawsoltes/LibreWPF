# Toolkit retained text continuation blocker — 2026-09-14

## Acceptance and reproduction

Application: **ProGPU.Wpf.ToolkitApp**, including actual AvalonDock controls.
Action: the existing live input gate, starting with transient-surface quiescence
after the first presented frame. This is a core delivery dependency, not general
Direct2D or typography expansion.

The unmodified Toolkit application builds in Release with zero warnings/errors
against the existing diagnostic graph. Its `MainWindow.xaml.cs` matches the
current source byte-for-byte (SHA-256
`fae9fc777a7a95cc371658b34f914656cbfd2eaba68ce2bf4905149e5c8c8be1`).
The runtime configuration selects `NativeMilWgpu`, with native hit testing enabled.
The graph deliberately overlays current source PresentationFramework/bridge and
ProGPU Backend/Native/native libraries, as in the earlier Showcase diagnostic.
It is **not final NuGet or SDK qualification**.

Two runs reproduce the failure before the unchanged 180-second live deadline.
Both reach a native presented frame at logical 980×640, pixels 1960×1280,
full-target viewport and DPI 2. Both then exit 134 due to an unhandled source
`PlatformNotSupportedException` during text arrangement. The second run uses a
source PresentationCore rebuilt with additional rejection diagnostics (also zero
warnings/errors); no admission, layout or fallback behavior was changed.

The retained continuation expects source index 38, and the new request correctly
supplies 38. The actual mismatch is width: retained paragraph width **217 DIPs**,
requested width **203.23666666666668 DIPs**, continuation line 1. This is not a
stale source index or a GPU query failure.

Trace:

```text
TextBlock.OnRender → TextBlock.Format → TextFormatterImp.FormatLineInternal
→ PortableTextLine.CreateContinuation → changed-width rejection
```

Source `TextBlock.Format` legitimately retries line formatting at its arranged
wrapping width to preserve measured line-break decisions. The current portable
adapter admits only continuations at exactly the original width, so normal
TextBlock layout can encounter an unsupported contract after first presentation.

Artifacts and bounded owned-process runner are retained under
`/Volumes/1TB-macOS/progpu-core-release.xtwndj/`:
`run-toolkit-diagnostic.sh`, `toolkit-diagnostic.log`, and
`toolkit-diagnostic-continuation-details.log`. Both owned application processes
are terminal. No existing artifacts were deleted and no deadline was extended.

## Required repair and acceptance

### Implemented continuation follow-up

ProGPU now provides native full-context continuation placement through an additive
C API, managed snapshot and optional `IPortableReflowTextParagraph`. The source
adapter uses that retained paragraph on width changes while preserving source
index validation, original source map/styles, original terminators and provider
ownership. The ordinary same-width path is unchanged. Measured inline objects
retain their original source identity; fragment/float reflow stays explicit.

Both native providers compile, all 19 native suites pass, and the managed consumer
passes 28 continued layouts plus boundary rejection and ordinary continued-collapse
checks. Both native export allowlists and generated contracts verify. Source
PresentationCore compiles without warnings; the bridge retains its one preexisting
unused-event warning. A typed source regression covers width changes after original
line/override disposal and original glyph source indices; its execution remains
pending at this checkpoint (the first unit build needed a fresh assets restore).

The unchanged Toolkit diagnostic now passes transient-surface quiescence and
reaches **filter focus**, then rejects a rectangle width during MIL translation.
Log: `toolkit-diagnostic-native-reflow.log` in the same external staging directory.
It exits 134; the full Toolkit gate has not passed. This is progress past the
reproduced continuation failure, not final package or later-action qualification.
The original requirements below remain the acceptance criteria, not an assertion
that all extended text contracts are complete.

Implement source-preserving native paragraph continuation at a new width through
ProGPU's existing C++ shaping/layout pipeline, then consume that explicit contract
in `PortableTextLine`. Retain the captured provider, complete source/physical-font
identity, original shaped-cluster boundaries, hidden document positions, modifier
scopes, tab origin, justification and continuation ownership after line/provider
override disposal. Source index mismatches must remain errors.

Do not simply ignore the width difference, increase a tolerance, force TextBlock
back to its measured width, drop wrapping or return an empty paragraph. Re-shaping
an isolated suffix without original paragraph context must not lose contextual
glyph or bidi semantics. Ordinary and inline measured continuations need explicit
native contracts; exclusion/float continuations must remain explicit until their
retained placement contracts can preserve the same semantics.

Required evidence is a focused native/source continuation regression, then the
same full live Toolkit/AvalonDock gate, including its separate presented floating
host and device-index input. Repeat against the final coherent packages, along
with Showcase, remaining SDK/platform gates and exact-head CI. The diagnostic
failure does not qualify package startup or any later Toolkit actions.

The rejection-message change applies to the source text adapter used by both
managed-portable and native-MIL modes. Windows MIL retains its existing formatter.
It is original source diagnostic code, not a renderer algorithm or a repair of
the missing contract.
