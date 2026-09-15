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
WinForms-local renderer change. All seven exact-head CI checks passed, and
#36 merged at `3fdc72bee10bb350a6f6ddfc0bfed637727d844d`. This branch
now pins that merge commit, rather than its former PR head; both the direct
and nested ProGPU gitlinks resolve to merged main
`7e6dd6a724240c6ec4f95ff1e3e7885cbe66035c`. WPF's own exact-head
canonical/package gate must rerun after this final source-graph repin.

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
bundle construction, but do not prove the Windows subphase. The exact guest
checkpoint below resolves the coarse Windows phase; individual operations
inside it and a completed application gate still require qualification.

### Windows ARM64 retained-bundle checkpoint

The corrected-head [ProGPU #168](https://github.com/wieslawsoltes/ProGPU/pull/168)
Build run `34991253884` uploaded `progpu-native-runtime-win-arm64` for head
`1dfda315e4b82c889de1b0a14eee90a2001204fa`. The downloaded
`progpu_native.dll` SHA-256 is
`f9d913ea13cf6f81d5b9382b23fc75066a398359533d9087754b4326432e41ca`.
The private Windows 11 ARM64 Parallels guest retained the previous installed
#167 DLL as `TestOutput\progpu_native-stages-ci26b2cdac.dll` before overlaying
the exact #168 native artifact. Managed WPF/ProGPU assemblies remain local
stage-enabled source overlays, so this is not assembled-package qualification.

The live Toolkit probe kept native MIL and its live input validation enabled,
with `PROGPU_NATIVE_TRACE_SCENE_ENCODE=1`,
`PROGPU_WPF_TRACE_NATIVE_LOOP=1`, and direct `cmd.exe` log redirection.
The cold `287`-command, one-span scene spent `6,033.006 ms` in resources and
`29,225.049 ms` in preparation, but only `0.536 ms` in bundle construction,
`0.644 ms` in replay, and `1.468 ms` in flush. The subsequent `6,842`-command
scene spent `5,642.654 ms` in resources, `80.817 ms` in preparation,
**`257,745.961 ms` building `327` retained spans**, `5.711 ms` in replay,
and `0.573 ms` in flush. This directly locates the recurring CPU delay inside
ProGPU's bundle-build path. It does not yet distinguish individual bundle
encoders, draw calls, mask/layer operations, or Dawn/D3D12 costs, and does not
prove that all spans can be safely reused across changed source generations.
The next source change must target that path without bypassing native input,
dropping scene identity, or switching to the managed compositor.

The third `6,862`-command scene spent `12.736 ms` in resources,
`30.869 ms` in preparation, **`267,411.853 ms` building `340` spans**,
`8.776 ms` in replay and `1.345 ms` in flush. The source scene again missed
the whole-scene bundle cache. The private Toolkit process then exited with
code `1` during popup validation: `WpfPortableNativePopupHost.EnsureInitialized`
rejected the selected Win32 native popup owner with
`PlatformNotSupportedException`. It had already passed frame/display geometry,
filter focus and filter text. The popup failure is a separate live native-owner
admission blocker; it must not be hidden by changing surface kind or accepting
an unowned popup. This run did not reach floating-window/redock success.

Final guest logs were copied to task-owned host cache as
`toolkit-encode-checkpoints-ci1dfda315-final-stderr.log` (SHA-256
`b7b27fff866b1f00cf92923f0de746b42ec1c93e928cfc5d301c023c246f8af5`)
and `toolkit-encode-checkpoints-ci1dfda315-final-stdout.log` (SHA-256
`b6fa650f960bd86306d38631732d4111a4dcb759f78948efa2f276ce47db5c3e`).
These paired logs preserve the source scene/generation evidence and the exact
failure stack. No passing floating-window, package or performance gate follows.

### Windows ARM64 mask-operation and popup-owner diagnosis

LibreWPF [#143](https://github.com/wieslawsoltes/LibreWPF/pull/143) passed all
ten exact-head checks and merged at
`a96ecf480209f37456378164744a83d56b7980e0` into the
`progpu-rendering-port` integration branch. That branch is not LibreWPF
`main`; this does not assert a final main-branch release.

The corrected ProGPU [#169](https://github.com/wieslawsoltes/ProGPU/pull/169)
Windows ARM64 CI run `34997024109` produced the bundle-operation profile DLL
for profile head `ac3ba5a21f728e401653004d6d44214ad7014f0c`; its SHA-256
is `f5556ef660dfa119751b0b448445e6adc4a840099212718f1e3851ea8e97e796`.
The rebased PR head is range-diff equivalent but its own CI is still pending.
The guest kept its previous #168 native DLL under the unique backup name
`TestOutput\progpu_native-checkpoints-ci1dfda315.dll` before this overlay.
The opt-in ProGPU popup diagnostic `ProGPU.Backend.dll` was also a local source
overlay, SHA-256 `3ee853b69938ca49ae09fa906a85628f8a6dff19578e6cd1bd5c16e61b9257b5`;
its previous guest DLL is retained as
`TestOutput\ProGPU.Backend-pre-popup-diagnostics.dll`. Native and managed
diagnostic overlays are not final assembled-package qualification.

With native MIL, native input, `PROGPU_NATIVE_TRACE_SCENE_ENCODE=1`,
`PROGPU_NATIVE_TRACE_POPUP_OWNER=1` and full Toolkit live validation enabled,
the changed `6,842`-command generation spent `261,171.423 ms` building
`327` retained spans. Eleven mask bindings accounted for `261,167.772 ms`.
Encoder creation, draw encoding, finish, release and other traversal together
were under `4 ms`; replay was `4.022 ms` and flush `0.408 ms`. The next three
changed generations each rebuilt eleven masks for about `253–271 s`, with
nonmask bundle operations still in milliseconds. This is direct Windows evidence
that the recurring CPU encode bottleneck is mask binding, not bundle encoder
creation or presentation. A separate macOS source-overlay kind profile of the
same Toolkit scene measured two vector-clip and nine picture masks, with
picture-mask child-engine creation dominant there. The Windows mask-kind and
child-engine subphase are not yet separately measured; do not infer their exact
split solely from macOS.

The Toolkit process exited `1` during the live popup step. The opt-in Win32
owner trace reported `NonlocalWindows`, but both passed nonzero values resolved
to `ownerThread=0`, `popupThread=0`, `ownerProcess=0`, `popupProcess=0` through
`GetWindowThreadProcessId`. Source inspection then found the bridge used
`nativeWindow.Win32.Value.Item2`. Silk.NET names that tuple
`(Hwnd, Hdc, HInstance)` in ProGPU's compiled backend source; `Item2` is an
HDC, not an HWND. The bridge now passes the named `Hwnd` for popup ownership,
activation, system menus and native drag. It does not weaken ProGPU's
same-thread/hidden Win32 admission or accept an unowned popup. A new exact
Windows live rerun and final package gate must prove the correction before
popup/floating-window qualification can be reported.

The final guest logs were copied into the task-owned host cache as
`toolkit-bundle-profile-ciac3ba5a2-final-stdout.log` (SHA-256
`96733195995027ea371d46e5235d70b087b7aec2c29e1bd9f278523a7accb834`)
and `toolkit-bundle-profile-ciac3ba5a2-final-stderr.log` (SHA-256
`746c2fe5b3b7311e08191d5489b3bd33049e2ae165805709ee065bb86e99c4d4`).
They retain scene/generation, measured operations and the exact rejected
native-handle pairing without publishing the private guest application tree.
