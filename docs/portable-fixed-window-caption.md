# Source-owned fixed-window captions

The X11 host advertises an optional typed `RequiresSourceFixedCaption` callback
using the actual initialized native window kind. Ordinary source `Window`s with
`ResizeMode.NoResize` and a decorated `WindowStyle` then own a caption visual.
This applies to all such windows, including the normal portable MessageBox.
There is no MessageBox-only template or application sample workaround.

The caption contains the live title and one normal source Close button. Closing
uses `Window.Close`, including cancellation and dialog lifetime. Caption dragging
uses the existing source `DragMove` path; the context menu uses the existing
owned native system-menu callback in desktop coordinates. Neither operation
changes modal input admission. Actual desktop behavior still needs validation.

The source keeps `Content`, its template and public `WindowStyle` intact. The
effective host style becomes borderless, and the new caption is an additional
visual child. Measure reserves its own 32-DIP height; arrange offsets only the
template child. This is an explicit owned UI dimension, never an inferred native
frame measurement. Native client/outer conversions still use the original live
frame callback. The source root remains at zero in the actual native client.
Title updates use the original dependency-property binding. Existing framework
control resources supply caption colors, text and button appearance.

Application `WindowChrome`, undecorated windows and other resize modes do not use
this caption. Changing the source policies removes or reinstates it and forwards
the effective style through the existing host callback. Hosts omitting the new
capability retain their native behavior. The provider's native owner, title,
modal hints, allowed functions and lifetime remain authoritative.

## Original failure and design basis

The original [GNOME capture run](https://github.com/wieslawsoltes/LibreWPF/actions/runs/39111922900)
shows a Minimize button on all four dialogs despite the correct client Motif
function/decoration restrictions. Its nine images and failed visual result remain
unchanged. The run identifies Mutter 50.1, but not the exact GTK binary version.

The reviewed [Mutter frame implementation](https://github.com/GNOME/mutter/blob/50.1/src/frames/meta-frame.c)
copies client title, closability and resizability into its GTK frame, without
copying modal/transient state during creation or subsequent property handling.
The reviewed [GTK window controls](https://github.com/GNOME/gtk/blob/4.22.0/gtk/gtkwindowcontrols.c)
base Minimize visibility on that GTK window state. This explains why another
client hint is insufficient evidence of a repair; it does not qualify an exact
binary attribution. No upstream implementation is copied into this feature.

## Validation still required

Six source tests pass for content/client layout, actual title binding, one Close
control, canceled versus completed close, style/resize/custom-chrome transitions,
host-callback reentrancy, size-to-content and hosts without the callback. The
unchanged ten-test portable modal gate also passes. The broader source run passes
84 tests; its preexisting native-Windows-only control is unavailable on the Mac
and the strict no-skips invocation records that as a failure. It remains required
in its original Windows lane. Source tests are not native evidence.
Both managed and native renderers consume the same ordinary WPF visual tree;
no renderer wire, shader, mask or source font policy changes. Both rendering
paths still require original application/package validation.

The host's local `Directory.Build.props` also supplies its own default
`ProGpuSourceRoot`, preserving caller overrides. Without it, direct host builds
resolved project paths under `/src` and could consume an older cached Scene.Native
package instead of the pinned source. MSBuild's evaluated project inventory now
resolves all eleven references to existing projects. The original mixed build
and its test result are retained but do not qualify the corrected dependency graph.
The corrected graph passes all 132 host activation tests with no skips. The six
renderer assemblies and rebuilt Interop in the test output match their source
outputs byte for byte; Scene.Native identifies the pinned `a37f2350` producer.
These are mock-host controls, not a native window or package qualification.

Before closing #112, repeat its unchanged actual GNOME captures with rebuilt
packages. Also validate real close, drag, modal blocking/restoration, title and
style transitions, size-to-content and DPI against actual source/native geometry.
Do not change global desktop button preferences, mutate another process's frame,
weaken original capture assertions or use local shared VMs.
