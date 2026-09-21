# Native WPF floating-row reference and actual application blocker

## Application evidence

The unchanged RealApplicationRunHarness built successfully (0 warnings/errors,
9.11 seconds) in the prepared source worktree. Running it with the native text
libraries failed during first Window measurement at
PortableDocumentParagraphSource.RequireInline, through FormatParagraph,
PortableFlowDocumentFormatter, FlowDocumentView and Application.Run. The exception
requires an explicit inline-object or anchored-block contract. Thus the remaining
anchor admission is an actual application blocker, not merely an inferred one.
The prepared tree contains mirrored source through 182b8e328 and older physical
submodules; this diagnostic is not exact-head package qualification.

## Windows reference

The independent eng/NativeAnchorReference program ran on the Windows 11 ARM64
Parallels guest with SDK 10.0.401 and Microsoft.WindowsDesktop.App 10.0.12. No
portable assemblies were loaded. The suspended guest was resumed; no configuration
or execution policy was changed. An initial PowerShell-file probe was rejected by
the guest policy and replaced by this normal C# program.

All 13 cases completed. Full observations are in
[the JSON record](native-mil-anchor-reference-2026-09-11.json). At a 320-DIP viewer
width, zero explicit insets and Consolas 12:

- The anchor-bearing row stays in place; the child starts below it at Y=14.05.
  Prefix and suffix text can share that preceding row.
- A prefix wrapping to the second row places the child at Y=28.1. Subsequent
  suffix rows flow beside the float and return to full width after its bottom.
- A full-width float occupies the next band; subsequent text clears it to Y=28.1
  rather than repeatedly moving its own source row.
- Two 100-DIP left floats pack at X=0 and 100 on the same band. Right floats pack
  at X=220 and 120. Center floats occupy X=110 and then X=5 (centered in the first
  remaining 110-DIP interval). The fixed-horizontal anchor placer is not this policy.
- Auto left Floater and auto Figure produce the same 72.5667-DIP exclusion edge
  in these cases; Stretch consumes the band. This does not prove all sizing cases.
- An anchor-only paragraph still has a native parent row: child Y=14.05. The
  native empty-shaped-input/no-row API must not be silently treated as that source
  paragraph contract.

These measurements supersede the speculative fixed-point placement approach.
They establish the tested bottomless cases only, not finite-page Figure behavior,
arbitrary fonts, RTL, boundaries exactly at a wrap, empty child boxes or clear-side
policy. Microsoft's [Flow Document overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/flow-document-overview)
also distinguishes freely fitting floaters from independently positioned figures;
the source FloaterParagraph path remains authoritative for bottomless Figure.

## Implementation consequence

ProGPU must finish a source-anchor-bearing physical row, place its ordered floats
in the free intervals below it, then activate their exclusions for later rows.
Use the existing native measured band writer and interval resolver. Do not add a
WPF-local line composer or a repeated whole-paragraph Y correction. Keep explicit
source event offsets and original child generations; hidden source ranges alone
do not let native fitting discover events. Empty parent rows and hard-segment
boundaries need explicit source metrics and ownership.

ProGPU commit 9beaaf2b adds distinct floating-interval placement while keeping
fixed anchor placement unchanged. Both native providers rebuild, the native text
suite passes and the named-module consumer compiles/runs. This C++ primitive has
no source consumer yet, so this checkpoint does not change the downstream gitlinks.
Its eventual batched C/managed transport and
native row-event fitting remain required before ordinary source admission. All
application, package, platform, input and final CI gates stay in force.
