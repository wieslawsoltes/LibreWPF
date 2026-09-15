# Native window frame sizing: source-to-host integration

The acceptance action is opening `ProGPU.Wpf.ShowcaseApp` and the shared native
text fixture as an ordinary, decorated top-level Window at 200% Windows DPI.
The fixture declares `Width="430" Height="300"`. In the Windows ARM64 VM,
stock WPF opened a 430 × 300 outer rectangle and a 417 × 264 client rectangle;
the prior ProGPU native MIL host opened 443 × 336 outer and 430 × 300 client.
That 13 × 36 surplus is a window frame applied *outside* an already
client-sized WPF outer request. The text line metrics alone could not reveal
this layout error.

The shared ProGPU frame contract reports native left/top/right/bottom as
desktop logical units. Win32 physical frame pixels are divided by the current
native content scale once; Cocoa/X11 GLFW frame metrics are already desktop
coordinates. `null` is unavailable; a known zero frame is valid for borderless
windows. No source path uses the portable presentation-source identity as an
HWND or invents title-bar dimensions.

LibreWPF now initializes its registered native host hidden before attaching
the Window root. It converts the Window's *outer* Width/Height to the host
*client* size after the native frame exists. The source Window measures and
arranges its child against the outer size minus the same frame. The portable
presentation source retains client size for surfaces and input, but adds the
frame exactly once when laying out the actual Window root; non-Window roots
and popups keep their previous client-sized layout. Reverse conversion during
SizeToContent measurement subtracts the frame exactly once.

The canonical `WindowsFormsIntegration` source gate requires LibreWPF and
LibreWinForms to consume an identical ProGPU commit. The first PR #141 CI run
rejected the new ProGPU pin before build because LibreWinForms still pinned
the preceding main commit. LibreWinForms PR #35 aligns its ProGPU submodule to
the same frame-contract commit; this LibreWPF branch pins that alignment. This
is a dependency fix, not a relaxation of the canonical gate.

The same-source Windows package-only text fixture now reports its actual
native outer/client rectangles, DPI, and declared Window size for both stock
WPF and native MIL. The gate compares the two rectangles (within one native
pixel), DPI, and source dimensions alongside text line metrics. In native
mode it reads the actual ProGPU Win32 host handle, never the portable
presentation-source identity; stock WPF uses its ordinary HWND. The earlier
443 × 336 oversized native window would fail this gate even though its text
line metrics passed.

The first exact-head SDK smoke with this source change compiled the real
PresentationCore/PresentationFramework and passed the source-built native MIL
host smoke, then failed in the real WPF XAML runtime harness before display.
Its trace showed `PortablePresentationSource.ApplyRootVisualLayout` invoking
Window's frame conversion while `TryCreateActivation` still had not returned
its activation to Window; `IsPortableWindowActive` was false and the old HWND
non-client helper was null. The registered normal-window path now creates the
source and native frame hidden, but defers the root visual attachment until
`Show`, after Window holds its portable activation. This uses the same
source-identity ordering as the existing hidden-window first-Show route.
Local ProGPU.Wpf compilation passes; the corrected runtime gate still needs
its own exact-head CI result. The failed smoke is not sizing qualification.

ProGPU backend contract tests passed (12/12) and the ProGPU.Wpf
portable host build completed with zero errors on macOS. Windows source
compilation passed on the prior PR head, but Windows x64/ARM64 stock-vs-native
rectangle validation, live
Showcase interaction, resize/DPI/chrome/SizeToContent transitions, and CI are
still required before this branch qualifies or merges. Existing native SDK
admission remains separate.
