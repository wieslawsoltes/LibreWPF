# Core native popup ownership connection

## Application and bounded outcome

The existing package Showcase's main menu and ComboBoxes create native Cocoa popup
surfaces through `WpfPortableNativePopupHost.EnsureInitialized`. Source inspection
found WPF-local Cocoa parent setup with no postcondition checks, and rejection
outside Windows could still proceed to Show. This batch moves checked Cocoa
parent setup into ProGPU and makes every selected native popup reject failed
owner configuration before Show. The sample and surface-selection policy are
unchanged. Findings are source-backed, not runtime reproduced.

The initial modal trace confirmed a separate limit: Apple's worksWhenModal
contract requires NSPanel subclasses, while the current host uses GLFW NSWindows.
The actual Showcase About view has no popup control. This batch therefore closes the
main-window popup setup branch, not genuine modal popup support or automatic
AppKit ShowDialog admission. Those remain open and guarded.

## Implementation

ProGPU `b56c949e` extends `NativePopupWindow.TryConfigureOwner` with Cocoa dispatch,
`CocoaPopupConfiguration` and source-generated native AppKit operations. It
requires main-thread ownership, matching registered native windows, actual host
views/delegates, a hidden popup and an acyclic owner chain. Window/view/delegate
and previous-parent references are retained across synchronous native callbacks.
Success requires observed parent, hidden state, deactivation flag and host
identity to match. Rejected setup attempts restoration without overwriting
reentrant foreign ownership; failed or partial restoration still requires caller
destruction. No Show, focus transfer, geometry conversion, class swizzle or modal
escape is performed.

LibreWPF now passes real Cocoa handles into this provider, removes its local
ownership routine and unused Objective-C imports, and disposes a selected native
popup when setup rejects or throws on Cocoa/X11 as well as Windows. The X11
adapter itself is unchanged. Both native MIL and managed portable rendering share
the same host path. WPF fixture expectations now protect ProGPU ownership and
all-platform rejection instead of requiring the removed local implementation.

See the [ProGPU contract and primary-source record](../external/ProGPU/docs/native-mil-cocoa-popup-ownership.md)
for algorithms, ownership, native ABI, cost and modal limits. This is native
platform control flow, not a new C++ renderer or GPU algorithm. It adds no pixel
readback, per-frame native operation or independent-lane CPU fallback. Setup is
O(W + D) for native window count W and parent depth D, with bounded retains and
constant managed scratch. No measured performance claim is made.

## Authored fixtures and build-only evidence

ProGPU policy fixtures cover success/repeat configuration; visible, stale and
cyclic admission; rejected parent/flag changes; restoration; reentrant host,
visibility and parent mutation; and callback exception propagation. WPF source
fixtures protect shared Cocoa dispatch and fail-closed setup before native Show.
These have not executed. Native AppKit behavior must be exercised by the real
package menu/ComboBox and native-host qualification lanes after feature freeze.

Standalone ProGPU compilation initially failed in NuGet ResolvePackageAssets
before source compilation. A matching .NET 10.0.201 forced restore succeeded,
then the complete ProGPU.Tests project compiled with 0 warnings/0 errors in
29.89 seconds:

```sh
/Users/wieslawsoltes/.dotnet/dotnet restore src/ProGPU.Tests/ProGPU.Tests.csproj --force --disable-parallel -v:quiet
/Users/wieslawsoltes/.dotnet/dotnet build src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Those commands ran from `external/ProGPU`. From the LibreWPF root, the repository
SDK compiled the bridge fixtures with 116 warnings/0 errors in 17.22 seconds:

```sh
./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

No tests, verifiers, applications, native AppKit calls, VM/GPU workloads,
benchmarks or CI polling ran. Native payloads were not rebuilt. Latest fetched
ProGPU main remains `102e39e5`, included by the feature branch. Unrelated native
scene/internal-test edits and performance-artifact deletions were excluded.

## Remaining delivery

Final gates must prove native setup on the AppKit main thread, visible placement,
nonactivating click/focus behavior, repeated popup close/reopen, native retain
release and explicit failed-setup behavior. Genuine NSPanel surface/input/lifetime
integration is still needed for modal popups; automatic AppKit sessions remain
guarded. Windows payload production/SDK admission, complete package consumption
and full application/image/lifetime/performance/CI qualification remain open.
