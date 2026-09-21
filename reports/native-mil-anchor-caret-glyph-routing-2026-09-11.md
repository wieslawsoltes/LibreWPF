# Anchored caret units and glyph export

The existing child text-view connection now also routes caret-unit movement into
the retained child's native TextLines. Exhausted child movement returns through
the original anchor's source boundary. Parent moves crossing a hidden anchor range
enter the original child content rather than skipping it. Source insertion lookup
is restricted to probing an adjacent anchor edge; ordinary character movement
continues to use native shaped cluster/caret results.

Glyph export traverses source ranges around children and recursively gathers the
actual retained child glyph runs. It does not reshape, clone or renumber glyphs.
A generation-local query set avoids duplicating a parent glyph-run object when
its source span crosses more than one queried source interval. Ordinary anchor-free
export retains its no-set path and the existing line-local GlyphRun convention.

Acceptance remains real Figure/Floater text navigation in the unchanged
RealXamlCompilerHarness through RealApplicationRunHarness. Vertical transitions
across anchor boundaries, automatic reference/convergence and actual anchored-viewer
runtime qualification remain open. Compiled routing and ordinary viewer regressions
must not be reported as full editing, glyph-position or package/platform parity.

Validation: final source build passed (0 errors, 1 existing warning, 33.29 seconds).
The macOS ARM64 source-host regressions passed, including ordinary rich viewer
navigation and retained anchor drawing. New anchored boundary/export branches
remain runtime-unqualified until the normal application path is connected.

Separately, the [strict MSVC job](https://github.com/wieslawsoltes/ProGPU/actions/runs/34545240173/job/103096319185)
passed on ProGPU 4c9a2fc5, confirming the explicit origin narrowing fix. This does
not establish completion of all PR checks or the remaining application gates.
