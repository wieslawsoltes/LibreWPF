# X11 About-dialog native-state integration — 2026-09-09

## Acceptance dependency and implementation

Application/action: open, Hide/Close and reopen the LibreWPF MVP About dialog.
Source inspection showed owner input/activation/native-close filtering already
present, but X11 ownership alone did not publish modal state to the window
manager. No application or VM reproduction ran during this build-first batch.

ProGPU `91104e9f` provides `NativeWindowModalHint` through the live
`SilkWindowController`. It is a borrowed native-window lease with host-thread,
duplicate-entry, disposal and failed-cleanup ownership checks. The X11 adapter
uses the actual native root, advertised EWMH support, bounded native-long atom
reads and owned-bit updates. Preexisting modal and unrelated state are preserved;
pending mapped-state removal cannot be mistaken for an externally owned bit on
rapid reopen. Unmapped-property merges retain bounded stack storage and reject
capacity overflow rather than create a set that cannot subsequently be read.

Entry failure rolls back a possibly partial update. Rejected/throwing cleanup
retains the lease rather than losing the native identity; removal requests are
idempotent. No foreign native classes, desktop-wide grabs, event-mask replacement,
application enabled-state mutation or alternate renderer were introduced.
Native property searches use runtime-intrinsic-capable span operations without
repacking. Scope sequencing and small protocol compaction are bounded control
work, not independent-lane image/geometry fallback kernels. No speed claim is made.

The LibreWPF host acquires the hint only on its actual X11 dialog path. It releases
before source input/focus completion, Hide and native destruction, including loop
finally and controller disposal. Source that hid synchronously before RunDialog
does not acquire a hint. Unsupported X11 hint admission throws rather than
silently reporting native modal support. Win32 input gates and Cocoa session
polling remain separate and unchanged.

The new admission failure exposed an existing source error path: ShowDialog could
throw while leaving the real Window visible. Source ShowPortableDialog now hides
through its own Window API before returning that error, preserves its activation
identity for retry, and still unwinds source modal/input ownership through the
existing release callback. A failed hide reports the original and cleanup errors
together. This change is shared by both renderer modes and leaves native Windows
MIL ShowDialog unchanged.

## Authored coverage and compilation

Portable lease fixtures cover initial native state, successful release, partial
entry rollback, exceptions, failed rollback/release ownership, idempotence,
foreign-thread rejection, pending WM observation, source gate/focus ordering and
LP64 native attributes. A native X11 fixture creates real hidden owner/dialog
windows, checks actual ownership/state preservation, duplicate leases, capacity
rejection, controller cleanup and separation from enabled-input capability.
It requires the explicit EWMH desktop lane documented in the ProGPU contract.

Bridge source-contract coverage requires hint release before native Hide,
destruction and source completion. Existing real source Window coverage now checks
failure-driven hiding, retained activation identity, retry, and combined host/hide
errors rather than manually hiding after every failed dialog loop.

Build-only commands, serialized with the workspace SDK:

```sh
./.dotnet/dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/ProGPU.Wpf.RealPresentationFrameworkHarness/ProGPU.Wpf.RealPresentationFrameworkHarness.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Backend/portable/native-X11 fixtures compiled with 0 warnings and 0 errors.
The bridge fixture graph initially compiled with 116 warnings and 0 errors; the
final incremental build reported 21/0. The source PresentationFramework test
graph initially compiled with 6 warnings and 0 errors; the final incremental
build including the combined-failure fixture reported 2/0. Unchanged targets were
reused; the lower warning totals are not warning fixes. The real source application
harness compiled with 1 warning and 0 errors. Fixtures are authored/compiled, not
executed; no runtime correctness is claimed.

## Remaining requirements

This closes the missing X11 hint producer/consumer connection, not full Linux
native input suppression. EWMH state is advisory; window-manager handling of
mapped dialogs, native chrome/input/focus, restart and nested popup behavior still
requires the final visible application lane. Wayland native modality, Cocoa native
popup admission, Windows native/managed payloads and SDK startup remain open.
All package, native/managed/native-Windows, GPU/SIMD, lifetime/performance and
exact-head CI gates remain required after feature freeze. No tests, verifier
scripts, application workloads, benchmarks or CI polling ran. The suspended
Parallels guests and stopped build-only Colima profile were not touched.

Original ProGPU X11 transport/controller and scope lifecycle code, plus the
existing LibreWPF source/host dialog path, are the implementation provenance.
No foreign implementation was copied. Both native MIL and managed portable share
the host changes; neither C++ nor managed scene/shader algorithms change.
Latest fetched ProGPU main is included. Unrelated native semantic-state changes
and performance artifact deletions are preserved and excluded from this batch.

Public protocol sources, adopted/rejected concepts and the explicit final X11
fixture command are in the
[ProGPU X11 modal-hint contract](../external/ProGPU/docs/native-mil-x11-modal-hint.md).
