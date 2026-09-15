# Win32 monitor geometry and native-MIL Toolkit qualification

## Merged package baseline

LibreWPF #141 merged at `8353784d1b9ba9f069a63550484cd15838989a4a`.
Its post-merge [Build](https://github.com/wieslawsoltes/LibreWPF/actions/runs/34975899940)
and Docs runs passed, including Windows x64 and ARM64 package-only native-MIL
Showcase jobs. ProGPU #166 and LibreWinForms #35 are also merged. Those gates
do not run the live Windows ARM64 Toolkit/AvalonDock floating-window sequence.

The private Parallels Windows 11 ARM64 rerun used the exact #141 package bundle
and source closure: LibreWPF `a1ab22d27d77c2f839d76a4a9c4d088f26111434`,
ProGPU main `3755428f3ad9c269a5e4a9bc91e0a4175f68f695`, and
LibreWinForms `4a1b17e` (full identity in the bundle manifest). All 23 package
SHA-256 values matched that manifest, and the unchanged Toolkit application
compiled with zero errors and warnings. Diagnostic guest files are under
`C:\Temp\LibreWPF141-a1ab22d27\TestOutput`; these are not published package
artifacts or final-head application qualification.

## Win32 full-screen versus work-area defect

The live Toolkit run first failed its `SystemParameters.PrimaryScreenWidth`/
`PrimaryScreenHeight` check. Silk.NET reported primary monitor `Bounds` as
`3592x1920`, while its `VideoMode.Resolution` was `3592x2016` at 2× DPI.
The first rectangle was the physical work area, with 96 physical pixels
reserved for the taskbar; the second was the full physical screen. The old
`MonitorBoundsAreLogical` heuristic treated their mismatch as evidence that
`Bounds` was already in DIPs, producing `3592x1920` for the source screen
instead of stock WPF's `1796x1008` DIPs. It also used work-area height as
full-screen height.

The follow-up keeps a typed full physical screen rectangle separate from the
GLFW work-area rectangle on Win32. It resolves the screen origin through GLFW
monitor position and dimensions through the monitor video mode, then marks
those explicit full bounds as physical. The existing public monitor mapping
overload and non-Windows heuristic are retained. Focused tests cover the
`3592x2016` screen/`3592x1920` work-area split and a left taskbar that changes
work-area origin without changing full-screen origin. The display-metrics test
requires `1796x1008` primary screen and `1796x960` primary work area in DIPs.
The first focused run passed 16/16 tests; this is a source-level checkpoint,
not a new package/runtime gate.

A **private managed-assembly overlay** in the same guest cleared this specific
display-metrics assertion: the Toolkit trace reached `frame ready`, reported
`logical967x605 pixels1934x1210 dpi2`, then reached `transient surface
quiescence` and `filter focus`. The overlay substituted a locally built
`ProGPU.Wpf.dll` into the exact-package output while retaining the package
original as `TestOutput\ProGPU.Wpf-exact.dll`. This is diagnostic evidence of
the monitor repair, not evidence that the new source/package closure passes
the complete Toolkit validation sequence.

## Separate C++ render-delay blocker

The same overlay run measured the first native-MIL C++ submission from
`741.028` to `29858.533` ms (~29.1 s). A later `NativeCompositor.RenderScene`
submission entered at `33716.118` and left at `295809.227` ms (~262.1 s).
The next submission entered at `298259.616` ms and had not returned when the
private probe was stopped. The trace is
`TestOutput\toolkit-overlay-trace-stdout.log`; stderr is beside it. These
timestamps locate the delay inside the synchronous native renderer call; they
do not identify the expensive C++ substage or prove a deadlock. The probe did
not reach floating-window/redock success. Its graceful close could not complete
while the native call was outstanding, so only that private Toolkit process
(PID 600) was forcibly stopped; the trace and test data were preserved.

The Toolkit startup probe now permits a bounded ~120-second first-frame wait
instead of ~19 seconds, avoiding a false early failure in this VM. This does
not make the ~262-second subsequent render acceptable. Next, isolate native
render phases in ProGPU C++ on the same engine/scene/generation and device,
then rerun a final-head package-mode live Toolkit sequence. Do not substitute
the managed compositor, skip input-index residency, relax floating-window
assertions, or treat a source overlay as release qualification.

The Windows native-MIL SDK default, full visual/DirectX/Direct2D parity,
commercial consumers, and the original broad goal remain open.

## Opt-in native stage capture follow-up

ProGPU [#167](https://github.com/wieslawsoltes/ProGPU/pull/167), from latest
main `3755428f3ad9c269a5e4a9bc91e0a4175f68f695`, adds size-checked,
opt-in native semantic-scene CPU phase metrics. Its local macOS ARM64 C++20
wgpu-native/Dawn build, native MIL/ABI tests, generated-contract verification,
and hardware-backed package-consumer capture run passed. Hosted CI and the
Windows ARM64 stage capture are still pending.

The WPF host follow-up requests that capture only when
`PROGPU_WPF_TRACE_NATIVE_LOOP` is enabled. It logs scene/generation,
command/draw/submission counts, payload hash and native CPU preflight,
resource, encode, flush, finalize and total durations after the host
`SurfacePresent` call returns. Ordinary frames keep the existing unmeasured
ProGPU overload. A local WPF product build against #167 source succeeds; the branch
must not publish or qualify that dependency until #167 passes and its exact
native package is available. The next VM run must preserve native selection
and the same Toolkit live assertions while recording these new phases.
