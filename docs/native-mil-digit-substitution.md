# Source number substitution through native text

WPF source text now carries the existing `DigitState` number policy into the
ProGPU C++ paragraph pipeline. This covers decimal digit sequences selected by
`NativeNational`, `Traditional`, `Context`, or `AsCulture`; `European` keeps the
source digits. `DigitState` continues to own culture selection, user overrides,
and the distinction between traditional and native-national digits.

## Font selection and source ownership

The source adapter retains the original text and source run boundaries before
selecting physical fonts. When contextual digits are present, the optional typed
`IPortableTextDigitContext` provider resolves the original hard segment in one
native call. An independent formatting request starting after source index zero
also reads preceding source spans back to the last hard break. Wrapped and
width-changing continuations retain their original paragraph and captured policy.
Cached non-text hard breaks retain explicit metadata; hidden source spans do not
erase that boundary. Preceding spans are combined before native UTF-16 decoding.

Active digit ranges use the existing `GlyphingCache` / `TypefaceMap` culture-aware
font linking. Composite-family ranges and mapped font scales therefore apply to
the digits that will actually be shaped. The resulting native styles carry a
resolved digit sequence or disabled substitution; native shaping does not make a
second contextual decision after the source has selected its physical faces.
Native grapheme starts keep combining marks, spacing marks, and joiners with
their source cluster while resolving those style intervals.

The paragraph input, source indices, hidden positions, brush ownership, and
editing/caret maps remain unchanged. A supplementary replacement digit retains
the original ASCII digit's one-code-unit source range. The native scalar record
owns that distinction. Embedded controls retain their existing non-ink style and
never participate in digit font lookup.

The ProGPU contract and native algorithm are described in
[native digit substitution](../external/ProGPU/docs/native-text-digit-substitution.md).

## Costs and limits

Paragraphs without contextual substitution do not allocate a context buffer or
call the native prepass. Contextual requests borrow two bytes per UTF-16 unit
from the shared array pool for context and grapheme starts, and return the buffer
after physical-font mapping. Preceding-context traversal has independent bounded
source and text budgets. The native context scan is
batched and shares the paragraph's contextual-state algorithm. Source linking
continues to use its existing cache; WPF does not implement another shaper.

Native digit sequences must contain exactly ten contiguous Unicode decimal
scalars. Invalid or non-contiguous custom sequences fail explicitly. A provider
without native digit-context resolution cannot accept contextual source policy.
The selected physical face must contain each replacement digit actually used.
Source alternate-character fallback, such as Tamil zero to ASCII zero, remains
explicit until the native mapping can retain that alternate scalar.

WPF's existing culture map also describes percent, decimal, and grouping
symbols. Active source ranges now carry their single-scalar mappings into the
ABI-5 ProGPU style record without changing source text, indices or caret
ownership. The native paragraph applies those mappings in its scratch copy
after preserving original source bidi. Physical font mapping runs first; if
the selected face lacks a number symbol, the source adapter uses
`DigitMap.GetFallbackCharacter` only when that face contains the alternate.
Multi-scalar culture symbols retain WPF's unchanged-source mapping behavior;
native replacement does not synthesize a multi-scalar glyph sequence. Digit
alternate-character fallback remains unsupported. Native-Windows comparison of
directional marks/punctuation and exact final package application evidence are
still required; this does not qualify full number-formatting or text parity.

## Verification

Source behavior regressions cover culture overrides, the three `AsCulture`
policies, mixed run policies, native context capability, LTR/RTL seeds, unchanged
source input, supplementary replacement digits, and retained wrapped continuations.
The native suite separately verifies actual substitution and source clusters.
Physical-font regressions use the repository's Traditional Arabic and Noto
Symbols 2 fonts with verified glyph coverage, including segmented supplementary
digits. A composite-family regression checks both the selected face and em scale.
Build and execution results are recorded in the worklog after running them on
the committed source and dependency revisions.
The [2026-09-22 qualification record](../reports/native-mil-source-digits-2026-09-22.md)
distinguishes the passing 53-test source runs on macOS/Windows, the corrected
native bidi metadata regression, and the still-required eight-case package comparison.
The [2026-09-23 number-symbol record](../reports/native-mil-number-symbols-2026-09-23.md)
separates the ABI-5 component and Windows source-test evidence from the still-open
Windows native/package comparison.

The culture-policy contract follows the public
[NumberSubstitutionMethod documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.numbersubstitutionmethod).
Source adapter changes reuse LibreWPF's existing policy and font-linking code;
the native implementation and provenance remain in ProGPU.
