# Native MIL bridge validation after main integration

## Acceptance path and production fixes

The acceptance path is the retained source application drawing through the shared
WPF bridge, including opacity masks and animated rounded/elliptical primitives.
The bounded production changes preserve the canonical Rect.Empty sentinel when
converting mask bounds back to managed Rect, while malformed negative bounds
still fail. Empty and a valid zero-sized source rectangle remain distinct.
Animated rounded rectangles and ellipses now apply their existing null brush/pen
no-op guard before counting unsupported animation state, matching ordinary draws.
Closed-context checks remain first.

## Full bridge test result

The complete Release ProGPU.Wpf.Tests assembly passes **1,754/1,754**, with zero
skips, on macOS ARM64. The build completes with zero errors and 20 analyzer
warnings. This validates the working patch on LibreWPF 017da89d4 with ProGPU
9e05651a and LibreWinForms c67b04a8; it is not exact-head package qualification.
Native library selection uses explicit build/runtime paths, not the original
dirty submodule directories.

Logs in the prepared validation worktree:

- artifacts/wpf-bridge-audit5-build.log
- artifacts/wpf-bridge-audit5-tests.log
- artifacts/wpf-bridge-final-stacks.log (earlier stopped run)

The monitor-geometry unit test now supplies its existing typed monitor fixture.
Its previous real GLFW call from the test worker was verified with a managed
stack before stopping that run. Real native-window testing remains in the host
and package gates; it is not replaced by the unit fixture.

Source audit assertions now retain the merged contracts: portable ownership
before native handles/input, typed activation callbacks, native hit-index
diagnostics, native caret release before disposal, complete pen-aware geometry,
registered text-provider justification, and terminating dispatcher frames.
The repository-file helper first identifies the source root, avoiding the nearer
test-project Directory.Build.props. Canonical WinForms and Windows runtime
dependencies are both required by the SDK assertion.

Retained tests distinguish known empty raster bounds from unavailable bounds and
actual geometry path strokes from rectangle commands. WinForms owner-draw checks
use actual system highlight/text colors and preserve antialiased focus coverage,
rather than hard-coded blue. No image comparison gate or tolerance is changed.

## Outstanding delivery gates

ProGPU 9e05651a's hosted checks are green except its exact Dawn/WebScene provider
test. The point/region fixture's summary versus returned-list ownership assertion
is under investigation. This remains a merge blocker.
The corrected full WPF unit suite does not replace exact-head source host,
SDK/package Showcase and required platform/application qualification. Wider
DirectX/Direct2D/Win2D requests remain deferred, not complete.
