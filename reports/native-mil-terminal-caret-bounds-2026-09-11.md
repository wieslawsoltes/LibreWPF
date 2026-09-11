# Terminal source caret bounds and application success

The failing TextBox caret query was measured in the prepared source application:
cp=18, length=1, bounds=0, PortableTextLine length=19, newline length=1,
width=98.71875 and height=13.798828125. It queried the terminal source newline,
not a missing shaped glyph or multiple run bounds. TextBoxLine correctly requires
one bounds result for the single source position.

PortableTextLine now returns a zero-width non-ink boundary rectangle when a positive
in-line source range maps to one shaping boundary. X comes from its existing
GetDistanceFromCharacterHit/provider caret path, Y stays line-local and height is
the retained source line height. Hidden edges and terminators do not gain glyphs,
paint, invented advances or independent shaping clusters. Ordinary nonempty ranges
still use native selection rectangles. The TextBox invariant is unchanged.

Temporary prepared-only diagnostic logging was removed before the final build.
The prepared Release application build succeeds in 57.13 seconds, zero errors,
one existing IDE0031 warning outside edited files. The unchanged
RealApplicationRunHarness then exits 0 with `Real WPF Application.Run smoke
succeeded.` This exercises the original startup application through the recording
host, including editor focus/caret after active access-key handling.

This is source application evidence on macOS arm64, not exact-head package/native
window/Windows/Linux qualification. Both renderers share the source line adapter.
Native MIL host regressions and final-head CI remain separate checks. ProGPU's
MSVC floating-test shadowing fix is pushed as 33070f23; its browser evidence-readback
timeout remains unresolved and must not be hidden by this application pass.

The final macOS native MIL host script also passes retention and host/device
recovery, including rich document, automatic anchors, source excluded TextLines,
selection and native text contracts, against the rebuilt source assemblies.
