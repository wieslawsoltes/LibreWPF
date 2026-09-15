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
and hardware-backed package-consumer capture run passed. All corrected-head
hosted PR checks, including Windows ARM64 native and package jobs, passed;
#167 merged to ProGPU main at
`7e6dd6a724240c6ec4f95ff1e3e7885cbe66035c`. The complete Windows
Toolkit application gate remains pending.

The WPF host follow-up requests that capture only when
`PROGPU_WPF_TRACE_NATIVE_LOOP` is enabled. It logs scene/generation,
command/draw/submission counts, payload hash and native CPU preflight,
resource, encode, flush, finalize and total durations after the host
`SurfacePresent` call returns. Ordinary frames keep the existing unmeasured
ProGPU overload. A local WPF product build against #167 source succeeds;
the follow-up branch now pins its merged main commit. Its own PR/CI and an
exact assembled-package Windows run are still required. The next VM run must
preserve native selection and the same Toolkit live assertions while recording
these new phases.

The first #143 canonical WinFormsIntegration CI attempt correctly rejected
the mixed source graph: LibreWinForms still pinned ProGPU
`3755428f3ad9c269a5e4a9bc91e0a4175f68f695`, while this branch pinned
`7e6dd6a724240c6ec4f95ff1e3e7885cbe66035c`. The companion
[LibreWinForms #36](https://github.com/wieslawsoltes/LibreWinForms/pull/36)
advances its nested ProGPU gitlink to the same merged main commit without a
WinForms-local renderer change. This branch pins that exact LibreWinForms
PR head for parallel CI; after #36 passes and merges, it must repin the
LibreWinForms merge commit and rerun its own exact-head canonical gate.

### macOS native-MIL source-overlay probe

The Toolkit sample was rebuilt with both `ProGpuWpfRendererMode=NativeMilWgpu`
and explicit `ProGpuWpfNativeMilHitTesting=true` admission. An isolated macOS
ARM64 application copy used the current WPF and ProGPU #167 managed assemblies
and locally built #167 `libprogpu_native.dylib`, with
`PROGPU_WPF_TOOLKIT_LIVE_VALIDATE=1` and
`PROGPU_WPF_TRACE_NATIVE_LOOP=1`. This is a source-overlay diagnostic, not an
exact hosted package or Windows/DirectX qualification.

The first run returned failure at the WindowControl text-entry assertion:
the input received focus but the expected `Pane` text remained empty. The
second run, additionally enabling `PROGPU_WPF_TRACE_INPUT=1`, delivered all
four text-input events and passed the complete live Toolkit/AvalonDock
sequence, including the floating editor. That disagreement is an intermittent
input/validation signal and remains open; one passing rerun does not erase the
first failure.

The stage trace proved that the native render path executed. A typical main
window frame with roughly 6,800 commands took about 125–140 ms in native
encoding, while several frames had 1.3–1.8-second encode outliers and some
had roughly 1.3-second flush outliers. Initial resource work was roughly
370 ms for the main host and subsequent resource stages were usually near
1 ms. These host-specific measurements do not explain the Windows ARM64
29/262-second stalls; the exact Windows native runtime and paired stage trace
are still required.

### Windows ARM64 exact native runtime phase trace

The corrected-head ProGPU #167 Build run `34984086884` produced the
`progpu-native-runtime-win-arm64` artifact for head
`26b2cdac871c6c013698784c529f536ef3a66919`. Its `progpu_native.dll`
SHA-256 was `d1126650383037470a48f7b3e3bfb835e2b1a4e26f32bba8f556a3776ee41523`.
The Windows 11 ARM64 Parallels guest retained a backup of its original native
DLL before installing this exact CI DLL. The stage-enabled managed assemblies
were local source overlays, so this is an exact-native-runtime diagnostic,
not a final assembled package or native Windows application qualification.

Direct `cmd.exe` file redirection kept the trace readable while the process
was running. A preceding PowerShell-redirection probe buffered all output;
after more than ten minutes of CPU-hot execution, a normal close request
could not be processed and only that private probe process was force-stopped.
Its empty log cannot support stage conclusions. The direct trace was copied
to the task-owned host cache as
`toolkit-cpu-stages-ci26b2cdac-stdout.log` while the subsequent scene was
still rendering.

The first presented scene (`287` commands, `32` draws, one submission) spent
`4,552.975 ms` in resource setup and `23,814.856 ms` in native encoding,
for `28,369.764 ms` native total. The second (`6,842` commands, `417` draws,
11 submissions) spent `7,753.788 ms` in resources and `255,426.716 ms`
in encoding, for `263,183.571 ms` total. Preflight and flush were near
1–2 ms. A third `6,860`-command scene measured `241,604.117 ms` encoding
with only `16.959 ms` resources and `0.545 ms` flush. The earlier
29/262-second host observations are therefore explained
primarily by a CPU-side ProGPU semantic-scene encode cost, not by WPF scene
update/compile, surface acquire, GPU submission or presentation. The source
invalidates retained render bundles on a whole-scene replay hash change;
that is a candidate cause, not yet a measured substage or completed fix.

The trace reached filter focus, filter text and popup validation after its
second scene. A normal window-close request was made during the third CPU-hot
render, and the diagnostic process returned after that scene without a full
validation result. The final guest trace was copied to the task-owned host
cache as `toolkit-cpu-stages-ci26b2cdac-final-stdout.log`. No full
Toolkit/AvalonDock result, subsequent-frame performance gate or Windows
DirectX/MIL parity claim follows from these measured frames.

The ProGPU `feature/native-semantic-encode-checkpoints` follow-up, rebased on
the merged #167 main commit, adds a second explicit
`PROGPU_NATIVE_TRACE_SCENE_ENCODE=1` admission and live standard-error
checkpoints for resource, preparation, retained bundle, replay and flush
subphases. Its default render path is unchanged. A local macOS ARM64 C++20
build and focused native MIL/internal/scene tests passed, as did the full
macOS native-MIL Toolkit/AvalonDock input sequence with checkpoints enabled.
On that host, a `6,842`-command scene built `330` spans in `166.157 ms` and
replayed them in `40.580 ms`. Those macOS values support investigating
bundle construction, but do not prove the Windows subphase. Hosted Windows
artifact and exact guest checkpoint capture remain required before choosing
an optimization.
