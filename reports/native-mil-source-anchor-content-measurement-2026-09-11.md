# Source anchor content measurement

The acceptance application remains the unchanged RealXamlCompilerHarness through
RealApplicationRunHarness. Its Figure/Floater content needs actual measured width
before automatic native anchor sizing can choose a fit-content width.

`WpfPortableDocumentFlow` now implements the optional measured document capability
using zero-copy descriptors and ProGPU's shared C++ arrangement call. Anchored
source layouts require that capability and retain its measured content width
separately from their constrained `Size`. Ordinary layouts keep measurement absent;
they do not publish fabricated zero or scan their lines in managed code.

The source fixture measures original Figure and Floater child paragraphs under a
4096-DIP constraint and requires positive content width below the constraint while
the allocated width stays 4096. Existing reflow and original source ownership
checks remain in the same fixture.

Validation: prepared source harness build passed (0 errors, 2 existing warnings,
1:39.34). The freshly built macOS ARM64 native MIL host smoke passed, including
both original anchor measurement checks, source excluded paragraphs and device
recovery. It compiled 23 commands/20 resources/9 draws and presented one frame.
The prepared checkout contains mirrored source changes and an older LibreWinForms
checkout; this is local integration evidence, not exact-head package provenance.

Dependencies: ProGPU a6c4c65af57afe0f2d53b9193175992715ad3277 and
LibreWinForms b92cfb10ff086aafc1c09efaa4c7c5990c90a243. Root physical submodule
worktrees are preserved; dependency updates use explicit verified gitlinks.

Automatic two-pass anchor sizing, placement/exclusion production and retained
child drawing/input ownership remain required. Parent AnchoredBlock admission
stays closed. This change does not qualify package startup, the complete unchanged
application, Windows/Linux behavior or final PR CI.
