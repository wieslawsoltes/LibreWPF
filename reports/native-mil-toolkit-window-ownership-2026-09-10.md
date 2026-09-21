# Toolkit source ownership and floating-window application closure

## Acceptance action and concrete blocker

Application: the unchanged free Extended.Wpf.Toolkit 5.1.2 / AvalonDock package
used by `samples/ProGPU.Wpf.ToolkitApp` and the SDK gate.

Actions: show the static Toolkit MessageBox with a Window owner and with the
IntPtr returned by that Window's `WindowInteropHelper.EnsureHandle`; float the
editor, focus it using its own host, and redock it.

Read-only inspection of the installed 5.1.2 assemblies established these call
paths (no third-party implementation was copied):

- `Xceed.Wpf.Toolkit.MessageBox.CreateContainer` assigns the supplied IntPtr to
  `WindowInteropHelper.Owner` before `ShowDialog`.
- `LayoutFloatingWindowControl.OnLoaded` calls `SetParentToMainWindowOf`; for
  this application's real Window root, that helper uses `Window.Owner`.
- The floating control then subscribes through its public HwndSource facade.
  Native caption-drag Win32 messages are a different path, not evidence of
  portable floating-window drag parity.

`Window.SetOwnerHandle` rejected every nonzero portable handle, including the
actual source identity returned by the registered ProGPU host. The existing
Toolkit handle-overload acceptance action therefore had an implemented-source
failure before dialog presentation. The old float live check only established
model membership, not a separate presented window or working input.

## Implementation

`Window` now resolves an owner handle only through the existing registered
`PortablePresentationSource` collection. The source must be live, on the child's
dispatcher, rooted in its actual active Window, and match both that Window's
activation handle and its current presentation source. The public HwndSource
facade is not an ownership authority. Unknown, mismatched, detached, non-Window,
cross-thread and stale sources are rejected. Ambiguous matches are rejected too;
no second handle registry or application-specific lookup was introduced.

Accepted identities go through `Window.Owner`, preserving self/cycle checks,
typed host rejection before collection mutation, and pre-Show owner state.
The portable owner getter returns the actual owner's current source handle.
Zero clears the existing typed relation. Native Windows MIL keeps its original
HWND implementation; portable selection is independent of the operating system.

The existing ProGPU bridge already returns its presentation-source identity from
the registered get-handle callback and implements owner changes using
`ProGpuWpfWindowHost.TrySetNativeOwner`. Its Show path applies the owner before
native visibility. This connection reuses those paths for both renderer modes;
there is no new WPF-local platform implementation, numeric fallback or ProGPU API.

## Authored coverage

- Source fixtures cover pre/post-child creation, owner getter and collections,
  host rejection, clearing, self/cycles, cross-dispatcher rejection, detached and
  non-Window roots, activation/source mismatch, closed and disposed identities.
  The pre-existing unknown-handle rejection fixture remains unchanged.
- Both static Toolkit MessageBox overloads must expose the same source Window
  owner and owned-window membership before the existing auto-close action.
- The live float action must find a distinct host through the actual editor's
  source Window, require presentation and correct owner, inject a click into
  that host, acquire editor focus, and confirm a device-resident input index.
  Explicit native SDK builds additionally require a native MIL session frame
  for main and floating hosts. Redocking must remove the floating host and
  return the editor to the main Window.
- Click injection now uses the receiving host's actual UIElement root. It does
  not translate a floating control into MainWindow's unrelated visual tree or
  perform desktop/framebuffer coordinate conversion.

## Build-only evidence and limits

The source PresentationFramework test graph compiled with 9 warnings / 0 errors
in 1:44.84. The complete Toolkit application's current C# and XAML were linked
into an isolated SDK build (the original Toolkit package, not a reduced mock).
NativeMilWgpu compilation completed with 0 warnings / 0 errors in 5.27 seconds;
ManagedPortable completed with 0 warnings / 0 errors in 4.28 seconds. The final
source test graph, including cross-dispatcher coverage, rebuilt with 2 warnings /
0 errors in 8.76 seconds. Both warnings are NU1701 for the existing
System.Private.Windows.Core.TestUtilities preview package's framework assets;
no warning suppression was added.

The isolated SDK compilation consumes the existing preview.45/.55 development
feed, which predates this Window fix and recent native input changes. It proves
sample compilation only, not exact-head package or runtime behavior. The source
test graph compiles the current Window implementation; its fixtures have not run.

No tests, application/VM graphics, native comparisons, benchmarks or CI polling
were performed. Automatic CI remains enabled. Exact-head package production,
native dialog/modal/input qualification, floating-window interaction and cleanup,
cross-platform fidelity, and both PRs' required green CI remain final gates.
This does not claim general HWND interoperability, title-bar drag emulation,
completed modality, full Toolkit input coverage or core feature freeze.

ProGPU main was refreshed from origin and remains
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, already an ancestor of the current
ProGPU feature branch. No ProGPU product change was needed for this source
ownership adapter; unrelated native work and artifact deletions were preserved.
