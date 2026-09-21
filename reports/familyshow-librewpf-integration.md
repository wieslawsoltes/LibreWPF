# FamilyShow LibreWPF Integration Report

Date: 2026-07-07

## Scope

FamilyShow at `/Users/wieslawsoltes/GitHub/FamilyShow` was switched to the LibreWPF SDK package and validated locally on macOS using the local preview feed version `0.1.0-preview.familyshow.1`.

The goal was to prove that a real WPF app can consume LibreWPF through the SDK package path, build with compiled XAML/BAML, and launch through the ProGPU/Silk.NET runtime without app-specific XAML workarounds.

## FamilyShow Integration

- `FamilyShow/FamilyShow.csproj`
  - Uses `Sdk="LibreWPF.Sdk/0.1.0-preview.familyshow.1"`.
  - Targets `net10.0-windows`.
  - Uses `AnyCPU` so the app can build on local `osx-arm64`.
  - Removes `UseWindowsForms`; the app does not use WinForms APIs, and keeping the WindowsDesktop framework reference makes macOS launch fail.
  - Adds `System.Drawing.Common` and `System.Resources.Extensions` for existing `.resx` bitmap/resource code.
- `FamilyShowLib/FamilyShowLib.csproj`
  - Uses the same LibreWPF SDK and target framework.
  - Removes `UseWindowsForms`.
- `NuGet.config`
  - Adds the local LibreWPF/ProGPU package feed at `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/FamilyShowLocal`.
- `Directory.Build.props`
  - Pins `ProGpuWpfSdkVersion` to `0.1.0-preview.familyshow.1`.

The original FamilyShow skin dictionaries were restored to their normal explicit same-assembly CLR namespace form:

```xml
xmlns:local="clr-namespace:FamilyShow;assembly=FamilyShow"
```

## LibreWPF Fixes Implemented

### Same-Assembly XAML/BAML Resolution

FamilyShow exposed a compiler/runtime mismatch in LibreWPF:

- `xmlns:local="clr-namespace:FamilyShow;assembly=FamilyShow"` failed at build time with `MC3074`.
- `xmlns:local="clr-namespace:FamilyShow"` could build but failed at runtime while loading compiled skin resource dictionaries from BAML.

The fix was made in LibreWPF source and SDK packaging, not in the app:

- `PresentationBuildTasks` now resolves project-local assemblies correctly during markup compilation when the XAML namespace names the current assembly.
- `PresentationFramework` now normalizes local assembly references and can resolve same-assembly BAML types such as `FamilyShow.ImageConverter`.
- The real XAML compiler/runtime harnesses now include a same-assembly resource marker regression path.

### Packaged SDK Build Tasks

The SDK package path was still able to pick stale or external PresentationBuildTasks assets. The SDK package now carries and selects the LibreWPF-built `PresentationBuildTasks.dll` under `tools/<tfm>/`, and the internal arcade target can rebuild/publish the local PBT copy when it is missing.

### Non-Windows Startup Compatibility

FamilyShow also hit non-Windows runtime compatibility gaps after the XAML fix:

- STA validation no longer calls Windows COM apartment APIs on non-Windows; the portable dispatcher owns thread affinity there.
- Legacy bitmap-effect STA validation is Windows-only.
- Composite font parsing no longer rejects all OS-specific sections on non-Windows; it selects the first recognized composite font section in deterministic WPF order.

### Startup Input Noise

The first rendered FamilyShow frame showed an unintended menu activation caused by an unmatched startup `MouseUp`. The Silk.NET input service now tracks pressed mouse buttons per native mouse and suppresses unmatched `MouseUp` events before they enter WPF routed input.

## Local Validation

Restore:

```bash
dotnet restore /Users/wieslawsoltes/GitHub/FamilyShow/FamilyShow/FamilyShow.csproj --force --no-cache -v:minimal
```

Result: succeeded.

Build:

```bash
dotnet build /Users/wieslawsoltes/GitHub/FamilyShow/FamilyShow/FamilyShow.csproj -v:minimal
```

Result: succeeded with 4 existing `FormattedText` obsolete warnings in `DiagramConnector.cs`.

Runtime:

```bash
export DOTNET_ROOT=/Users/wieslawsoltes/GitHub/wpf/.dotnet
export DOTNET_ROLL_FORWARD=Major
export DOTNET_ROLL_FORWARD_TO_PRERELEASE=1
/Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet run \
  --project /Users/wieslawsoltes/GitHub/FamilyShow/FamilyShow/FamilyShow.csproj \
  --no-build -v:minimal
```

Result: FamilyShow launched and stayed running. A screenshot was captured at `/tmp/familyshow-librewpf.png`.

Focused bridge regression validation:

```bash
dotnet vstest /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  --TestCaseFilter:"FullyQualifiedName~SilkNetWpfInputServiceTests"
```

Result: 39 tests passed.

Real XAML runtime harness:

```bash
dotnet run \
  --project /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.RealXamlRuntimeHarness/ProGPU.Wpf.RealXamlRuntimeHarness.csproj \
  -c Release --no-build
```

Result: `Real WPF XAML runtime smoke succeeded.`

## Remaining Follow-Up

- Decide whether `LibreWPF.Sdk` should fail fast with a clear diagnostic when apps set `UseWindowsForms=true`, since that currently requests `Microsoft.WindowsDesktop.App` and is not portable on macOS.
- Add migration documentation for apps that use `.resx` bitmap resources and therefore need explicit `System.Drawing.Common` / `System.Resources.Extensions` references.
- Continue validating FamilyShow interactive flows: menus, open/save dialogs, GEDCOM import, sample loading, diagram editing, animation, printing/XPS paths, and image resource paths.
- Continue ProGPU rendering fidelity work for richer WPF scenes after the app-level startup path is now unblocked.

## Windsor Sample Validation Update

The packaged app was retested with `/Users/wieslawsoltes/GitHub/FamilyShow-SampleWindsorFamilly.zip`.

Additional LibreWPF/ProGPU fixes made for this lane:

- Popup replay now has a retained popup layer and non-Windows `Popup` positioning calls `UpdatePosition()` after the popup root is attached.
- Portable popups now synthesize the `HwndSource.AutoResized` behavior that Windows popups rely on. `Popup` attaches a non-Windows `PopupRoot.LayoutUpdated` handler, updates the typed portable popup client size through the existing popup service, and feeds the resulting size through the normal `OnWindowResize`/`Reposition` path. This keeps menu, combo box, context menu, tooltip, and other popup content measured/repositioned after the first attach without adding FamilyShow-specific code or bridge reflection.
- Source-built text now maps common WPF/Windows families such as `Calibri`, `Segoe UI`, and `Consolas` to deterministic portable fallbacks, so FamilyShow skin text no longer disappears.
- The SDK native compatibility shim exports `GetOpenFileNameA/W` and `GetSaveFileNameA/W` as `comdlg32.dll` on macOS/Linux package outputs; FamilyShow's app-level `CommonDialog` P/Invoke can now be satisfied without modifying app code.
- `SecurityHelper.MapUrlToZoneWrapper` no longer calls Windows COM URL-zone APIs on non-Windows, which removes the `CoInternetCreateSecurityManager` crash path hit by image loading.

Current validation observations:

- FamilyShow builds in package mode with the local `0.1.0-preview.familyshow.1` SDK feed.
- The refreshed local package feed includes the portable popup auto-resize/reposition fix in `LibreWPF.Transport.0.1.0-preview.familyshow.1.nupkg`.
- The refreshed FamilyShow build launches and renders the welcome/recent-file state from the packaged SDK path; capture: `/tmp/familyshow-refreshed-popup-fix.png`.
- The Windsor sample can load and the main diagram renders nodes, labels, connectors, gradients, scrollbars, and the right-side family list through ProGPU.
- The top-level `Open` command path works with the `comdlg32` shim and can load the staged Windsor file.
- The renderer still creates several auxiliary 39px native surfaces plus one 500x500 surface. These are likely host/window lifetime artifacts and should be investigated before preview polish.

Popup validation notes:

- The latest popup rendering bug was a ProGPU composition ordering issue: popup roots were replayed, but the popup retained branch was not guaranteed to be composited above the main retained/flat WPF drawing layers. `ProGpuWpfCompositionTarget` now owns a dedicated popup retained root, and `ProGpuWpfDrawingFrame` composes layers in this order: main retained WPF, flat drawing root, popup retained WPF. This keeps menus, combo boxes, dropdowns, and direct `Popup` content above the owner window surface without adding app-specific code.
- `ProGpuWpfDiagnostics.TryGetCompositionLayerSnapshot(...)` now exposes the retained/flat/popup layer order and popup child count for live package-mode validation.
- The Showcase app live validation now opens a top-level menu popup, a `ComboBox` dropdown, and a direct `Popup`, and asserts that each one reaches the ProGPU retained popup layer above the main drawing layer. The local run passed through `PROGPU_WPF_SHOWCASE_LIVE_VALIDATE=1 ./eng/run-progpu-wpf-showcase.sh`.
- A bounded FamilyShow package-mode startup run launched the refreshed apphost from `/Users/wieslawsoltes/GitHub/FamilyShow/FamilyShow/bin/Debug/net10.0-windows/FamilyShow` for 10 seconds with no runtime exception output in `/tmp/familyshow-librewpf-run.log`.
- The focused bridge build passed with `./.dotnet/dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal -m:1`.
- The focused popup/source platform guard passed with `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --TestCaseFilter:"FullyQualifiedName~WpfManagedProjectGraphTests.RealPresentationFrameworkSmokeGuardsNativePlatformEntrypoints"`.
- The source-built `PresentationFramework` transport rebuilt successfully after the popup change. It still reports the existing ReachFramework `System.Collections.Immutable` / `System.Reflection.Metadata` version conflict warnings from the current local toolset, unrelated to the popup code.
- FamilyShow restore/build passed after clearing the local NuGet cache for the refreshed preview package. The only FamilyShow warnings are the existing `FormattedText` obsolete overload warnings in `DiagramConnector.cs`.
- Synthetic click automation from this Codex session is currently blocked by macOS event permissions: command-line `CGEvent` clicks did not reach the app input trace, and AppleScript activation was denied with `Not authorised to send Apple events to System Events`. Manual interactive validation should still verify top menu dropdowns, welcome recent-file activation, combo boxes, context menus, and tooltips against the refreshed package.

Remaining FamilyShow-specific blocker:

- The original Windsor sample stores paths like `Images\Prince Charles.jpg`. On macOS, `System.IO.Path.Combine(...)` treats backslash as a literal filename character, not a directory separator. FamilyShow's own load code therefore does not copy photos/stories into `LocalApplicationData/Family.Show/CurrentFamily`, and the selected-person avatar area remains blank even though LibreWPF `BitmapImage` can decode the image when handed an existing path.
- A package-mode bitmap probe using the same LibreWPF SDK successfully decoded and copied pixels from both normal and backslash-named file paths, so the current avatar failure is not an imaging decoder failure.
- This is not a XAML compiler issue. It is either an app portability issue or a future LibreWPF SDK compatibility feature if we decide to provide broader Windows-path migration support for no-source-change ports.

Useful captures:

- `/tmp/familyshow-current-main.png`: Windsor loaded with the diagram visible.
- `/tmp/familyshow-open-menu-click.png`: command-loaded Windsor view after the `comdlg32` shim path.
- `/tmp/familyshow-after-manual-photo-paths.png`: manually staged backslash photo paths; avatar still blank because the original binding had already failed and selection was not reliably changed.

The next LibreWPF-side follow-up should be a small package-mode image-control smoke app that renders a real JPEG through `Image.Source` and screenshots the result. That will lock down the ProGPU `DrawImage` path independently from FamilyShow's path migration behavior.
