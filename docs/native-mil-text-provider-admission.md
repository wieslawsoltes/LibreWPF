# Native text provider admission

## Problem

Portable `TextFormatterImp` already routes admitted text through the typed
`IPortableTextFormatting` service and ProGPU's retained native C++ paragraph.
When no provider was registered, it also attempted `SimpleTextLine`. If that
source was not representable by the simple path, `CreatePortableFallback`
returned a synthetic one-character end-of-paragraph line. The call appeared to
succeed while losing the source text, objects, selection positions and width.

That result was not a rendering fallback: it was an empty replacement for
content that required shaping. Intrinsic measurement repeated the same behavior
and could publish zero-like widths for an unmeasured source.

## Admission contract

Portable formatting now uses this ordered policy:

1. A registered `IPortableTextFormatting` service owns the complete paragraph,
   including its original UTF-16 source mapping, styles, clusters and interaction.
2. Without that service, `SimpleTextLine` may still return a line only when its
   existing source-aware implementation represents the complete input.
3. If the simple path rejects the source, line formatting and intrinsic-width
   measurement throw the same `PlatformNotSupportedException`. They never create
   an empty line and never enter Windows LineServices from portable media.

Explicit portable selection on Windows requires the provider before source
construction because source font and shaping ownership must not cross into the
Windows-MIL resource domain. Managed Windows-MIL selection is unchanged.

## Implementation

`TextFormatterImp` owns one missing-provider exception factory so line formatting
and intrinsic measurement cannot drift to different fallback behavior.
`SimpleTextLine.CreatePortableFallback` and its synthetic
`CreatePortableEndOfParagraph` helper are removed. Ordinary provider-less simple
text behavior remains unchanged.

The product host continues to register `WpfPortableTextFormatting`, whose request
flows into ProGPU's `NativeTextParagraphSnapshot` and native C++ paragraph API.
This change therefore protects the existing full source path; it does not add a
second shaping engine to PresentationCore.

## Coverage and limits

The focused source test supplies a fixed embedded object without a provider and
requires both public line formatting and intrinsic measurement to reject it with
the source-preservation error. The managed graph guard rejects restoration of
either empty-fallback helper.

Local source validation used the repository-pinned .NET 11 preview SDK. The
`ProGPU.Wpf.Tests` graph built with zero errors and its focused source guard passed
1/1. `PresentationCore.Tests` built with zero errors after restoring its explicit
build-task dependency, and the focused formatter behavior regression passed 1/1
on macOS ARM64. Existing project warnings were unchanged.

This contract does not by itself implement variable-size embedded objects,
directional modifier embeddings, number substitution, custom tab collections,
display-mode hinting or other semantics that the typed provider still rejects.
Those features must be added to the shared ProGPU paragraph contract rather than
hidden behind a provider-less line.
