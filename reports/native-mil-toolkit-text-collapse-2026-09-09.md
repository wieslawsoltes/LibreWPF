# Toolkit header collapse: implementation checkpoint

## Concrete acceptance dependency

- Application: existing package-mode `ProGPU.Wpf.ToolkitApp`, unchanged.
- Action: narrow its window so the top header exceeds its available width.
- Source: `samples/ProGPU.Wpf.ToolkitApp/MainWindow.xaml:107` sets the real title
  and `TextTrimming="CharacterEllipsis"` at line 111.
- `PresentationFramework/MS/Internal/Text/Line.cs` invokes `TextLine.Collapse`
  for drawing, hit/bounds queries and collapsed width. Its collapsing properties
  use the source paragraph's actual default run properties.
- `PresentationCore/MS/internal/TextFormatting/PortableTextLine.cs:532` rejects
  overflowing collapse. No registered provider currently exposes a collapsed view.

This is source-backed, not a runtime reproduction. The Toolkit header remains a
core blocker. Do not remove trimming from its XAML or claim that native utility
compilation closes this source application path.

## Implemented prerequisite

ProGPU `bf7a0fb4` corrects its existing shared C++ trimming scan. Resolved tab widths
use the forward grid policy, and retained cuts respect shaping-safe boundaries
instead of distinct cluster ids alone. Per-style scales remain intact. The scan
is linear, dependency-ordered CPU work with constant scratch; existing NEON/SSE2
metric paths are unchanged. Both ordinary and logical/tab-aware entry points use
the shared helper. No WPF-local truncator, shaper or renderer was introduced.

Native regression coverage was authored for removed/retained tabs, origin and
style-scale changes, unsafe boundaries, sign-only output, untouched output tail,
and ordinary/logical entry-point equivalence. The native test executable and text
library compiled successfully from an isolated clean detached checkout at that
commit (four compile/link steps, exit 0). Tests were **not executed**.

Command from the LibreWPF repository:

```sh
cmake --build artifacts/native-core-build.KvxVug/build-osx-arm64 --target progpu_native_text_tests --parallel 3
```

This was the existing macOS ARM64 strict C++20 header-compatibility configuration.
Other platform/module builds, full artifact production and runtime qualification
remain open. Existing staged native binaries were not replaced with this build.
Unrelated ProGPU scene/internal-test edits and performance-artifact deletions were
excluded and preserved. Fetched ProGPU main remains `102e39e5`, already included
by the feature branch. No tests, verifiers, applications, VM GPU work, benchmarks
or CI polling ran.

## Next implementation on this same application blocker

Implement a typed collapsed view over the original retained native paragraph,
with actual collapsing-symbol face/metrics/brush, explicit hidden source ranges,
correct RTL visual placement and caret/selection/bounds mapping. Preserve source
line length, styled runs, decorations and continuation lifetime. The untruncated
snapshot must not be casually reused: deriving cluster ends from only visible
glyphs incorrectly assigns hidden text to the last visible cluster. Address
native synthetic-symbol identity and oversized single-cluster behavior as part
of that contract. Then connect source `PortableTextLine.Collapse` and author the
actual Toolkit/source regression path. See ProGPU's
[design and primary-source record](../external/ProGPU/docs/native-mil-text-collapse.md).

The preceding Showcase document scan found its current simple paragraphs, hyperlinks
and list policy already routed. Source MinOrphanLines/MinWidowLines defaults are
zero, not an unimplemented nonzero constraint; no gratuitous paginator expansion
was started. Font availability and actual document output still need final gates.
