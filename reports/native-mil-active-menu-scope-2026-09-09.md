# Core MVP menu entry: active portable source

## Acceptance dependency

Application: existing package-mode `ProGPU.Wpf.MvpApp` with `_File` and `_About`
menu items. Action: clear element focus, press F10/Alt, then navigate the active
window's menu; do not target an inactive owner while a dialog blocks it.

Source inspection found `KeyboardNavigation.OnEnterMenuMode` calls user32
GetActiveWindow whenever the event has no source. That is invalid on macOS/Linux
and cannot resolve ProGPU-owned HWNDs into native WPF HwndSource on Windows.
AccessKeyManager's separate non-Windows fallback selected the first registered
root regardless of activation, visibility, thread or modality. These are
source-backed findings, not reproduced runtime failures.

## Implemented connection

ProGPU `2c1d9459` adds the neutral `IPortableAccessKeyScopeSource` interface and
[design/contract record](../external/ProGPU/docs/native-mil-access-key-scope.md).
It carries a direct boolean query, not an allocating window-state snapshot or a
request for native activation. Source WPF Window supplies actual existing
portable ownership, visibility, disposal, host-fed IsActive and shared ProGPU
modal admission. Both WPF renderer modes consume the same source behavior.

Source AccessKeyManager now resolves eligible current-dispatcher portable sources,
rejecting missing or conflicting eligibility rather than using creation order.
Its default scope and KeyboardNavigation's no-focus menu-entry branch share that
resolver. Frozen portable media never falls through to user32; native Windows
MIL keeps its original HWND route. Custom roots must explicitly supply the typed
capability. Explicit element/popup scopes and native activation/focus policy are
unchanged. No reflection, source-shape probes or renderer workaround was added.

Lookup remains O(S) over the existing weak source snapshot enumeration. Each root
query is constant work; no new cache, geometry work, GPU call or per-root snapshot
is added. Dispatcher/identity/eligibility decisions are dependent object control
flow, not an independent SIMD/GPU workload. No speed claim is made.

## Authored fixtures and compilation

PresentationCore covers two portable roots, actual active selection, default key
dispatch, conflicting activation, detached/disposed roots and missing capability.
PresentationFramework covers source Window activation, no-focus F10 entry,
visibility, hide/show/deactivation, modal blocking and close. Portable-only tests
explicitly skip the separately selected Windows-MIL lane; they do not switch a
frozen backend. Bridge source-contract fixtures protect the shared resolver,
ownership/modal guards and absence of direct user32 menu-entry lookup.

Compilation only, on macOS ARM64 with the repository SDK:

```sh
./.dotnet/dotnet build src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
./.dotnet/dotnet build src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Results: PresentationCore 9 warnings/0 errors (58.56 seconds);
PresentationFramework 2 warnings/0 errors (6.05 seconds). These builds include the
new neutral interface. No test executable or application ran.

The bridge/source-contract fixture project also compiled: 116 warnings/0 errors
(17.74 seconds), using the same switches:

```sh
./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Fetched ProGPU main remains `102e39e5`, included by the feature branch. Unrelated
ProGPU scene/internal-test changes and performance-artifact deletions remain
untouched. No native payload was rebuilt for this managed source-input change.
No tests, verifiers, VM/GPU workloads, benchmarks or CI polling ran.

## Open delivery gates

This closes the identified no-focus source-import/default-scope branch, not
native platform focus/modality qualification, all keyboard/menu behavior or
Windows package admission. Final qualification must exercise actual package MVP
F10/Alt with cleared focus, multiple windows/dispatchers, dialog blocking, popup
focus restoration and native Windows/portable comparisons. Existing SDK gates
remain intact. Continue the core startup/package and application closure queue;
do not expand unrelated input/DirectX families from this checkpoint.
