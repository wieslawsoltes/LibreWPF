# LibreWinForms ProGPU Port

## Decision

Adding `wieslawsoltes/winforms` as a submodule helps the SharpDevelop and broader compatibility work. The correct long-term path is to reuse upstream WinForms managed code in a LibreWinForms port rather than keep adding application-specific WinForms stubs inside LibreWPF.

The submodule is mounted at:

`/Users/wieslawsoltes/GitHub/wpf/external/LibreWinForms`

The active port branch inside the submodule is:

`librewinforms-progpu-port`

## Package lane

Initial `LibreWinForms.*` package identities were added in the submodule:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.WindowsFormsIntegration`

The LibreWPF SDK now has `ProGpuWpfUseLibreWinForms`. When a package-mode app sets both `ProGpuWpfUsePortableWinFormsCompat=true` and `ProGpuWpfUseLibreWinForms=true`, the SDK prefers the `LibreWinForms.*` package identities instead of the old `LibreWPF.WinFormsCompat.*` package identities.

`LibreWinForms.System.Windows.Forms` and `LibreWinForms.WindowsFormsIntegration` are now source-owned in the submodule. They build the portable compatibility implementation directly into framework-identity assemblies (`System.Windows.Forms.dll` and `WindowsFormsIntegration.dll`) instead of depending on `LibreWPF.WinFormsCompat.*` alias packages. The SDK package remains the stable package identity for future no-source-change WinForms app switching.

The local SharpDevelop feed also has refreshed `LibreWPF.Sdk` package content so `ProGpuWpfUseLibreWinForms=true` resolves the source-owned `LibreWinForms.*` packages. A normal SDK repack still needs the repository private restore feeds configured for the WPF pack project; the local validation feed was refreshed from the checked-in SDK target files.

## Reuse plan

Move reusable managed code from upstream WinForms into LibreWinForms packages in this order:

1. Resource and design-time APIs needed by SharpDevelop: `ResXResourceReader`, `ResXResourceWriter`, `System.Drawing.Design`, `System.Windows.Forms.Design`, and `System.ComponentModel.Design` types.
2. Control/data models used by SharpDevelop pads and FormsDesigner: `Control`, `ContainerControl`, `UserControl`, `Form`, `TreeView`, `ListView`, `PropertyGrid`, menu/context-menu, dialogs, data binding, and image lists.
3. `WindowsFormsIntegration` host contracts so WPF-hosted WinForms surfaces use the same popup/input/render path as LibreWPF.
4. Platform services for Silk.NET windows/input, ProGPU painting/composition/text/clipping/hit testing, local OS dialogs/clipboard/drag-drop, and Win32 compatibility shims where APIs need Win32 semantics.

## SharpDevelop validation

This should help SharpDevelop because many of its projects reference `System.Windows.Forms` and `WindowsFormsIntegration` directly. A branded SDK/package lane lets us keep those references stable while moving implementation from a WPF-local shim into a WinForms source-reuse port.

The local SharpDevelop LibreWPF build has now been switched to:

```xml
<ProGpuWpfUseLibreWinForms>true</ProGpuWpfUseLibreWinForms>
```

Local source-owned packages were packed into:

`/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`

Fresh-cache package restore confirms the SharpDevelop full-workbench restore graph now contains:

- `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1`
- `LibreWinForms.WindowsFormsIntegration/0.1.0-preview.sharpdevelop.1`

Validated results against `/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln`:

- `SharpDevelop.Full.LibreWpf` Release build succeeds with warnings only.
- WPF workbench main menu popup opens.
- WPF workbench AddInTree context menu opens with 27 items.
- WPF ComboBox drop-down opens.
- Hosted WinForms `PropertyGrid` smoke succeeds with 22 rows.
- Hosted WinForms `ContextMenuStrip.Show(...)` opens with 3 items.
- FormsDesigner add-in compiles, is included in the full workbench output, attaches `ICSharpCode.FormsDesigner.FormsDesignerViewContent` to `LineCounterBrowser.cs`, and loads a replayed `System.Windows.Forms.UserControl` design surface with 21 components.

The FormsDesigner load fix is split between SharpDevelop and LibreWinForms compatibility code. SharpDevelop's LibreWPF parser path now adds `System.ComponentModel.TypeConverter`, `System.Drawing.Primitives`, portable `System.Windows.Forms.dll`, and ProGPU `System.Drawing.Common.dll` assemblies by typed runtime identity when classic .NET Framework projects cannot resolve WinForms/Drawing references on macOS. The existing SharpDevelop designer rule still checks normal `System.Windows.Forms.Form`/`UserControl` base types. The portable `CodeDomDesignerLoader` then replays common CodeDOM designer statements into typed controls/components and modeled collections, which is enough for the LineCounter designer surface to load beyond attachment.

Regression coverage:

- `ProGPU.Wpf.Tests.Platform.PortableWinFormsCodeDomDesignerLoaderTests.BeginLoadReplaysCodeDomControlTree` validates `DesignSurface` CodeDOM replay for `UserControl -> Panel -> Button`.
- The adjacent portable WinForms compatibility tests still pass under the repository `vstest` path.

## 2026-07-08 source-owned package validation

The copied implementation in `external/LibreWinForms/src/LibreWinForms.Portable` now builds as source-owned packages:

```text
LibreWinForms.System.Windows.Forms build        -> succeeds, 26 warnings, 0 errors
LibreWinForms.WindowsFormsIntegration build     -> succeeds, 4 warnings, 0 errors
LibreWinForms.System.Windows.Forms pack         -> creates System.Windows.Forms.dll package
LibreWinForms.WindowsFormsIntegration pack      -> creates WindowsFormsIntegration.dll package
SharpDevelop.Full.LibreWpf fresh-cache build    -> succeeds, 286 warnings, 0 errors
```

Runtime smoke through the source-owned package lane:

```text
FormsDesigner smoke -> Attached, surface=Loaded, root=System.Windows.Forms.UserControl, components=21, selectable=21
Designer mutation   -> Success, selected component reaches PropertyGrid and changed Text value is visible
Main menu popup     -> opened
Context menu popup  -> opened, 27 items
ComboBox popup      -> opened
PropertyGrid smoke  -> Success, selected=CSharpProject, rows=22
ContextMenuStrip    -> Opened, 3 items
```

## 2026-07-08 ProGPU drawing shim update

The next SharpDevelop fidelity gap was in the shared ProGPU drawing layer rather than LibreWPF or SharpDevelop code. `System.Drawing.Graphics.DrawImage(...)` now supports source-rectangle sprite extraction and `ImageAttributes.ColorMatrix` through the ProGPU image-effect shader path. This covers SharpDevelop ghost icons, grayscale code-coverage bitmaps, and version-control overlay images without app-specific fallbacks.

The local validation feed was refreshed for:

- `ProGPU.Scene`
- `ProGPU.System.Drawing.Common`
- `ProGPU.Dxf`

`ProGPU.Dxf` now suppresses the transitive Microsoft `System.Drawing.Common` asset from `netDxf.netstandard`, so ProGPU samples/tests and LibreWinForms consumers see one source-owned drawing shim assembly.

Validated results:

```text
ProGPU.Tests build                                  -> succeeds, 4 warnings, 0 errors
GdiBitmapTests + ImageEffectRenderTests             -> 14 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release build -> succeeds, 286 warnings, 0 errors
SharpDevelop combined smoke                         -> menus/popups/build/resx/forms designer/property grid/context menu/completion all pass
```

The fresh-cache validation used:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-librewinforms-source-owned-1
```

This is important because earlier same-version preview packages in the global NuGet cache did not contain the newer ProGPU interop dialog DTOs or System.Drawing types. Package-mode CI should keep using a clean package cache or unique preview versions for this lane.

## 2026-07-08 FormsDesigner CodeDOM flush and ToolStripContainer serialization

The portable LibreWinForms design surface now retains its loader and dispatches `DesignSurface.Flush()` into `CodeDomDesignerLoader.Flush()`. The loader serializes the live design-surface component graph back into a `CodeCompileUnit` and calls the SharpDevelop C# designer generator's normal `Write(...)` path. This keeps SharpDevelop on its existing managed designer pipeline instead of adding app-specific save logic.

The serializer also handles `ToolStripContainer` intrinsic child panels through typed expressions such as `this.tscMain.BottomToolStripPanel`, rather than expecting `TopToolStripPanel`, `BottomToolStripPanel`, `ContentPanel`, and related panels to exist as named fields. This fixes the LineCounter designer flush path where a named `ToolStrip` is hosted inside `BottomToolStripPanel`.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 3 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
FormsDesigner smoke                                         -> Attached, surface=Loaded, components=21, selectable=21
FormsDesigner mutation smoke                                -> Success, selectedByService=True, selectedByContainer=True, selectedByGrid=True, valueVisible=True, flushPersisted=True, rows=54
```

## 2026-07-08 FormsDesigner event hookup preservation

The portable `CodeDomDesignerLoader` now captures existing `CodeAttachEventStatement` and `CodeRemoveEventStatement` entries from the parsed `InitializeComponent()` method and appends them to the generated CodeDOM during `Flush()`. This prevents designer round trips from dropping existing handler hookups such as `button.Click += ...` or SharpDevelop sample handlers like `ListView.ColumnClick += ...` while the serializer still rebuilds the mutable component/property/child-control graph from typed WinForms objects.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 4 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner event property editing

The portable design host now publishes a typed `IEventBindingService`. `CodeDomDesignerLoader.BeginLoad()` seeds that service from parsed `CodeAttachEventStatement`/`CodeRemoveEventStatement` entries after the design surface has replayed the component tree, and `PortableCodeDomDesignSurfaceSerializer` emits the current event-binding service state during `Flush()`. This means designer event-property edits can replace existing handler hookups instead of the loader blindly replaying stale parsed statements. The host also prefers an app-provided event service over the fallback portable service, which lets SharpDevelop's active C# FormsDesigner service keep owning compatible-method lookup, handler generation, and source navigation.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 6 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 115 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner component lifecycle

The portable `IDesignerHost` now also owns the basic component lifecycle expected by toolbox and delete flows. Unnamed `CreateComponent(...)` calls get a stable `INameCreationService` name such as `label1`, the name is published through the component site and `IDesignerSerializationManager`, and the generated CodeDOM can serialize the new component field, properties, and parent `Controls.Add(...)` relationship. `DestroyComponent(...)` now detaches controls from their parent collection, removes name/instance mappings, clears portable event bindings for the component, disposes it, and prevents stale fields, child-add statements, or event hookups from being emitted on the next flush.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 8 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner resources and localization

The portable CodeDOM replay now handles the resource patterns emitted by WinForms designers. `ComponentResourceManager.GetObject(...)`, `GetString(...)`, `GetStream(...)`, and `ApplyResources(...)` are evaluated through typed resource-manager APIs, and `ApplyResources(...)` has a property-descriptor fallback over the resolved `ResourceSet` for portable controls whose platform implementation does not mutate all properties itself. CodeDOM type names are normalized before type-resolution service and known-type lookup, so assembly-qualified designer type names resolve consistently.

The serializer also reads the active `CodeDomLocalizationProvider` from the portable design host and emits `resources.ApplyResources(this, "$this")` plus per-component `resources.ApplyResources(...)` calls when `CodeDomLocalizationModel.PropertyReflection` is requested. This preserves the reflection-localization shape that SharpDevelop detects in existing designer files while the remaining resource-file writer path is brought over from upstream WinForms.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 10 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner handler generation contract

The portable `EventBindingService` now follows the standard designer flow for `IEventBindingService.ShowCode(component, event)`: when no handler is assigned, it creates a unique method name, publishes it through the event-property binding map, calls the app-provided code navigation/generation override, and rolls the new binding back if navigation fails. This lets SharpDevelop's `CSharpEventBindingService` keep owning real handler insertion and source navigation while the portable host supplies the expected WinForms service contract.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 11 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 FormsDesigner ResX resource file support

LibreWinForms now owns a portable `System.Resources` ResX compatibility slice in the source-owned `System.Windows.Forms.dll` package: `ResXFileRef`, `ResXDataNode`, `ResXResourceReader`, and `ResXResourceWriter`. The implementation covers the designer resource-file API surface SharpDevelop expects without depending on Windows WinForms assemblies: string and byte resources, metadata enumeration, `UseResXDataNodes`, comments, node round trips, relative file references through `BasePath`, and standard type-name converter hooks.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsResXResourceTests + designer resource tests -> Passed, 5 total
LibreWinForms.System.Windows.Forms package-mode build                         -> succeeds, 28 warnings, 0 errors
```

The package-mode build used a refreshed `LibreWPF.Interop/0.1.0-preview.sharpdevelop.1` package and an explicit `LibreWinFormsBridgePackageVersion=0.1.0-preview.sharpdevelop.1` so the source-owned LibreWinForms project resolved the current ProGPU dialog and service DTO contracts. The next SharpDevelop validation step is to remove or disable its transitional local `System.Resources.ResX*` shim in the LibreWPF wrapper, because those types now correctly belong to LibreWinForms.

SharpDevelop package-mode validation after disabling that transitional local shim:

```text
SharpDevelop.Full.LibreWpf fresh-cache Release build -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                             -> popups/resx/build/forms designer/property grid/context menu/completion all pass, exit code 0
```

The broad smoke used the refreshed local `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1` package and verified the package-owned ResX API through `LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1`.

## 2026-07-08 PictureBox designer initialization compatibility

`LibreWinForms.System.Windows.Forms.PictureBox` now implements `ISupportInitialize` with the standard no-op initialization contract required by generated WinForms designer code. This unblocks SharpDevelop's historical `ExceptionBox.InitializeComponent()` and other designer-generated `((ISupportInitialize)pictureBox).BeginInit()/EndInit()` patterns without adding app-specific shims.

Focused validation:

```text
ProGPU.Wpf.Tests PictureBoxSupportsDesignerInitialization -> Passed
LibreWinForms.System.Windows.Forms package-mode build     -> succeeds, 28 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build      -> succeeds, 286 warnings, 0 errors
SharpDevelop full smoke                                   -> popups/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

The package-mode `LibreWinForms.System.Windows.Forms` build must restore with a fresh `NUGET_PACKAGES` cache and the local LibreWPF/ProGPU feed so `LibreWPF.Interop` and `ProGPU.System.Drawing.Common` are extracted before compile:

```text
NUGET_PACKAGES=/tmp/librewinforms-local-nuget
RestoreSources=/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal;https://api.nuget.org/v3/index.json
LibreWinFormsBridgePackageVersion=0.1.0-preview.sharpdevelop.1
```

## 2026-07-08 LibreWinForms package workflow bridge bootstrap

The LibreWinForms repository now follows the LibreWPF preview-shipping shape more closely:

- GitHub default branch is `librewinforms-progpu-port`.
- Repository description and topics are set for LibreWinForms, WinForms, ProGPU, Silk.NET, cross-platform, .NET, and SDK discovery.
- The README front section has split NuGet tables for main LibreWinForms packages and bridge packages, with NuGet badge columns.
- CI and release workflows build matching LibreWPF/ProGPU bridge packages from `wieslawsoltes/wpf` branch `progpu-rendering-port` before packing LibreWinForms.
- The release workflow accepts an optional bridge version and still creates GitHub releases for `librewinforms-v*` tags with generated notes.

- Docs verification now checks the bridge-package documentation and workflow bootstrap text.

`LibreWinForms.WindowsFormsIntegration` can now restore in package mode by depending on `LibreWPF.Transport` when no explicit `LibreWpfManagedAssemblyRoot` is provided. The old direct WPF assembly references remain available only for local artifact-root validation. This makes standalone LibreWinForms CI/release usable after the matching bridge packages are built in the workflow.

`LibreWinForms.System.Windows.Forms.Control` now exposes the standard typed `Validating` and `Validated` event surface and raises validation before `Leave` during focus loss. The WPF fallback compatibility package mirrors the same event contract for package consumers that have not moved to LibreWinForms yet.

Validation:

```text
LibreWinForms docs verification                                           -> succeeds
LibreWinForms package lane, fresh NuGet cache + SharpDevelopLocal feed    -> succeeds; packages, manifest, bundle, checksum written
SharpDevelop.ResourceToolkit package-mode build                           -> succeeds, 155 warnings, 0 errors
SharpDevelop.Full.LibreWpf ResourceToolkit-included build                 -> succeeds, 39 warnings, 0 errors
SharpDevelop broad smoke                                                  -> popups/resx/build/forms designer/property grid/context menu/completion all pass, exit code 0
```

Public-feed-only LibreWinForms packing is still expected to fail until `ProGPU.System.Drawing.Common` and the matching LibreWPF bridge packages are published for the selected preview version. The release order stays ProGPU first, then LibreWPF bridge packages, then LibreWinForms.

The first pushed LibreWinForms bridge-bootstrap workflow exposed one extra CI-only issue: when WPF is checked out below the LibreWinForms repository, the generated ProGPU Avalonia package-smoke project inherited the parent repository's Central Package Management settings and failed restore with `NU1008`. `eng/progpu-avalonia-package-smoke.sh` now writes a local `Directory.Packages.props` with `ManagePackageVersionsCentrally=false` beside the generated smoke project, so the package references with explicit versions remain isolated from parent checkout policy.

The same parent-checkout issue also affected WPF repo projects that intentionally carry explicit `PackageReference Version=...` metadata. The WPF repo root now has a `Directory.Packages.props` boundary with CPM disabled, while ProGPU and LibreWinForms submodules keep their own nearer CPM files. This keeps nested bridge builds isolated without removing CPM support from package consumers.

Validation:

```text
PresentationBuildTasks Release build -> succeeds, 0 warnings, 0 errors
ProGPU Avalonia package smoke        -> succeeds, 0 warnings, 0 errors
```

The next LibreWinForms bridge rerun reached the real WPF `Application.Run` harness and exposed a stale harness invocation of `PortableWindowActivationService.Register(...)`. The service gained the optional `setWindowRegion` callback, and reflection invocation still requires the optional slot to be supplied. `ProGPU.Wpf.RealApplicationRunHarness` now passes the explicit trailing `null` callback value.

Validation:

```text
Real WPF Application.Run harness -> succeeds
```

The following LibreWinForms rerun cleared the real `Application.Run` harness and then exposed the same stale registration call in the package-mode SDK switch runtime harness. `ProGPU.Wpf.SdkSwitchRuntimeHarness` now also passes the explicit trailing `null` callback value so the nested LibreWinForms bridge build can run against the current portable activation service contract.

After both harness registration fixes, the nested LibreWPF bridge build passed and LibreWinForms packaging reached its own package restore. The remaining CI blocker was that `ProGPU.System.Drawing.Common` and its `ProGPU.SkiaSharp` dependency were not produced into the local bridge feed even though LibreWinForms consumes them as preview package dependencies. ProGPU now lists both packages as official packages, and the LibreWPF SDK CI feed now packs `external/ProGPU/src/SkiaSharp/SkiaSharp.csproj` and `external/ProGPU/src/System.Drawing.Common/System.Drawing.Common.csproj` with the correct package IDs and audit assembly mappings.

The standalone LibreWinForms package lane then exposed a local reproducibility issue: an older same-version `LibreWPF.Interop/0.1.0-preview.1` in the user/global NuGet cache could shadow the freshly built bridge feed and hide newly added typed ColorDialog/FontDialog DTOs. LibreWinForms commit `2810c93a8` fixes this in `eng/librewinforms-pack.sh` by defaulting restore to `artifacts/nuget/librewinforms-pack` and evicting the current LibreWinForms plus bridge package versions before restore. The README and release docs now document `LIBREWINFORMS_NUGET_PACKAGES` for alternate cache locations.

Validation:

```text
LibreWinForms docs verification                                                       -> succeeds
LibreWinForms package lane with isolated cache + local LibreWPF/ProGPU bridge feed     -> succeeds; manifest records winFormsHasTrackedChanges=false
WPF superproject submodule update                                                     -> points external/LibreWinForms at 2810c93a8
```

## 2026-07-09 LibreWinForms repo shipping-shape verification

The LibreWinForms repository settings were re-applied idempotently through `gh repo edit`:

- default branch: `librewinforms-progpu-port`
- description: `LibreWinForms: cross-platform WinForms-shaped APIs and SDK backed by ProGPU/Silk.NET.`
- topics: `librewinforms`, `winforms`, `progpu`, `silk-net`, `cross-platform`, `dotnet`, `sdk`

No source or workflow file changes were needed in `/Users/wieslawsoltes/GitHub/winforms`: the README already has the LibreWinForms front section, SDK migration steps, split NuGet package tables with badge columns, bridge-package table, build/release instructions, and the original upstream README at the bottom. The existing GitHub workflow set also already matches the LibreWPF shape:

- `.github/workflows/librewinforms-ci.yml`
- `.github/workflows/librewinforms-docs.yml`
- `.github/workflows/librewinforms-release.yml`

Validation used the current `0.1.0-preview.1` LibreWPF/ProGPU bridge packages from `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/Release/NonShipping` and wrote temporary package artifacts outside the repo:

```text
./eng/librewinforms-verify-docs.sh
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.1 \
LIBREWINFORMS_BRIDGE_PACKAGE_VERSION=0.1.0-preview.1 \
LIBREWINFORMS_PACKAGE_OUTPUT=/tmp/librewinforms-pack-validation \
LIBREWINFORMS_NUGET_PACKAGES=/tmp/librewinforms-pack-nuget \
LIBREWINFORMS_RESTORE_SOURCES=/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/Release/NonShipping\;https://api.nuget.org/v3/index.json \
./eng/librewinforms-pack.sh
```

Results:

```text
LibreWinForms docs verification               -> succeeds
LibreWinForms.System.Windows.Forms package    -> created
LibreWinForms.WindowsFormsIntegration package -> created
LibreWinForms.Sdk package                     -> created
Preview package manifest                      -> created
Preview release bundle                        -> created
Preview release bundle SHA-256                -> created
WinForms repository working tree              -> clean
```

## 2026-07-10 WinForms application host bootstrap

The WPF superproject updated `external/ProGPU` to `main` commit `4a25075ec56a9dbff2a0a789e524cb5117ed9bf5` and aligned `external/LibreWinForms` to branch `librewinforms-progpu-port` before adding new WinForms host work. No ProGPU source changes were made in this pass.

LibreWinForms now has a typed `IWinFormsApplicationHost` seam in `System.Windows.Forms`. `Application.Run(Form)`, `Form.ShowDialog(...)`, and `Application.ExitThread()` route through that seam when WindowsFormsIntegration registers a host; otherwise the existing fallback behavior remains. `Form.Close(CloseReason)` now returns cancellation state so the WPF host can honor `Closing` and `FormClosing` cancellation without reflection or Win32 callbacks.

`WindowsFormsHost.EnableWindowsFormsInterop()` now registers the WPF-backed application host and also registers LibreWPF portable activation through `LibreWPF.ProGPU`. This makes `Application.Run(Form)` use the existing ProGPU/Silk.NET WPF window path instead of raw `HwndSource`/`user32.dll`. The WPF host defers `Form.Show()` until the WPF window is loaded and marshals close/title updates to the owning WPF dispatcher.

The LibreWinForms SDK now defaults `LibreWinFormsUseWindowsFormsIntegration=true` when `UseWindowsForms=true`, generates a small module-initializer bootstrap that calls `WindowsFormsHost.EnableWindowsFormsInterop()`, and adds the WPF support package closure required by PresentationFramework (`System.Configuration.ConfigurationManager`, `System.Formats.Nrbf`, `System.IO.Packaging`, and `System.Windows.Extensions`) when WindowsFormsIntegration is enabled.

Source validation was added under `src/LibreWinForms.Portable/LibreWinForms.SdkSmoke`. The smoke imports the local SDK, references current LibreWPF managed artifacts, references current ProGPU submodule projects for source-mode DLL coherence, and verifies both startup bootstrap and `Application.Run(Form)` show/close routing.

Validation results:

```text
LibreWinForms.SdkSmoke Release build -> succeeds, 2 warning groups from source-built WPF assembly-version conflicts, 0 errors
LibreWinForms.SdkSmoke startup run   -> LibreWinForms SDK smoke build loaded.
LibreWinForms.SdkSmoke --run-form    -> LibreWinForms SDK smoke result=Success host=WPF formShown=True formClosed=True
```

The first windowed smoke attempts exposed two packaging/runtime alignment issues that are now documented release requirements rather than WinForms code changes:

- The public `0.1.0-preview.1` bridge packages are behind current source for typed dialog DTOs, so source validation used `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` with `LibreWinFormsBridgePackageVersion=0.1.0-preview.sharpdevelop.1`.
- After updating the ProGPU submodule, stale package-mode `ProGPU.Scene.dll` conflicted with newer `ProGPU.Wpf.dll`. Source smoke now references the current ProGPU submodule projects to validate the updated source state. Before preview release, LibreWPF/ProGPU/LibreWinForms package feeds must be regenerated from matching commits so package-mode apps do not mix old ProGPU runtime assemblies with newer LibreWPF host assemblies.

## Remaining work

- Replace the copied compatibility implementation with progressively reused upstream WinForms managed code from the submodule, keeping the same typed ProGPU/Silk.NET platform seams.
- Replace the remaining compatibility-only controls with upstream managed WinForms implementations behind typed ProGPU/Silk.NET platform services.
- Expand FormsDesigner runtime validation from the current load/selection/property mutation/minimal CodeDOM flush/event-preservation/event-property editing/component create-remove/resource-replay/localization-shape/handler-generation-contract/ResX-reader-writer/toolbox-create-remove/selected-tool pointer-placement coverage to real SharpDevelop source-navigation smoke, resource-file save/load round trips, visual move/resize behavior, and broader generated-code round trips.
- Make the standard WPF SDK pack workflow restore from the required private WPF feeds so local validation does not need generated package artifact refreshes.

## 2026-07-10 WPF-owned modal forms

The source-owned LibreWinForms WPF host now accepts both LibreWinForms `Form` owners and external `IWin32Window` owners. For the latter, it resolves the typed LibreWPF `HwndSource` associated with the portable handle and assigns that source's root `Window` as the generated form window owner. This keeps owner resolution out of reflection and avoids treating a synthetic portable handle as a native Win32 HWND.

`LibreWinForms.SdkSmoke --run-dialog` now validates owner loading/linkage, `Shown`, `FormClosed`, and synchronous `DialogResult.OK`. The package-mode SharpDevelop lane validates the same contract with its real `ExceptionBox` and `SD.WinForms.MainWin32Window` owner. The corresponding WPF portable modal loop and ProGPU/Silk input-lifetime hardening live in LibreWPF; the ProGPU submodule did not need a host-specific change.

Validation:

```text
LibreWinForms.SdkSmoke --run-form    -> Success
LibreWinForms.SdkSmoke --run-dialog  -> Success host=WPF ownerLoaded=True dialogShown=True dialogClosed=True ownerLinked=True result=OK
SharpDevelop ExceptionBox smoke      -> Success controls=10 ownerLinked=True presentationSource=True result=OK
```

## 2026-07-10 source-owned designer sites

The portable `DesignSurface` no longer puts components in a detached plain `Container`. `PortableDesignerHost` now derives from the managed component container, creates design-mode sites that resolve host services through typed `IServiceProvider`, and supports per-site `IServiceContainer`/`IDictionaryService` state. This reuses the upstream WinForms `DesignerHost : Container` and host-aware `Site` architecture while keeping the portable implementation free of the old nested-container reflection bootstrap.

The design surface now disposes its host, components, and site-local service containers. Site renames update serialization-manager name/instance maps and emit the standard component-rename event. This lets SharpDevelop's replayed controls resolve `IComponentChangeService` from `component.Site`, allows normal property descriptors to notify the designer, and keeps CodeDOM serialization on the real current component graph.

Validation:

```text
LibreWinForms.SdkSmoke --run-designer    -> Success; load/site services/local state/selection/serialization/rename
SharpDevelop focused FormsDesigner       -> Success; 21 components, 54 rows, changed Text persisted
SharpDevelop broad package-mode run       -> exit code 0 with FormsDesigner, PropertyGrid, AvalonDock, popups and owned form
```

## 2026-07-10 nested designer sites and service containers

LibreWinForms now completes the upstream-managed nested-site pattern used by WinForms designers. Every portable designer site lazily owns a typed `INestedContainer`; its service container chains site-local services to the containing nested site and then to `PortableDesignerHost`. Nested components receive design-mode `INestedSite` instances, owner-qualified names, host service lookup, component lifecycle events, recursive name refresh after owner rename, and deterministic subtree/service disposal.

`IDesignerSerializationManager.GetName(...)` and `GetInstance(...)` now understand nested full names without property probing or private method invocation. A nested site's `ISite.Container` remains the nested container while `GetService(IContainer)` and `GetService(IServiceContainer)` resolve the designer host, matching upstream WinForms behavior.

The SDK designer smoke now validates `INestedContainer.Owner`, `INestedSite.FullName`, adding/added events, inherited `IDesignerHost`, `IContainer`, `IComponentChangeService`, site-local service lookup, serialization lookup, and descendant name updates after renaming the owner. The result is fully successful.

SharpDevelop's historical `ComponentContainerSetUp(...)` reflection bootstrap was removed. It now requests only the public `INestedContainer` contract; LibreWinForms performs service initialization inside the framework.

Validation:

```text
LibreWinForms SDK designer smoke             -> Success; all root and nested site assertions true
SharpDevelop fresh-cache package-mode build  -> succeeds, 286 warnings, 0 errors
Focused LineCounter FormsDesigner             -> Success; 21 components, 54 rows, flushPersisted=True
Broad SharpDevelop package-mode run           -> exit code 0
Broad runtime coverage                        -> menu/context/combo/toolbar popups, build, ResX, FormsDesigner,
                                                 PropertyGrid, AvalonDock, ContextMenuStrip, ExceptionBox, completion
Native input lifetime                         -> balanced attach/detach; no runtime failure marker
```

No ProGPU source change was needed for this managed designer contract.

## 2026-07-10 designer container lifecycle and popup replacement

The portable design host now centralizes the upstream managed component-container lifecycle. `Container.Add(...)`, `IDesignerHost.CreateComponent(...)`, loader-created components, and direct toolbox-style additions share naming, root registration, serialization lookup, and adding/added events. Main and nested removals keep `IComponent.Site` valid through removing/removed callbacks, then clear selection/name/event state and dispose the site before unsiting. `DestroyComponent(...)` removes through the component's actual container, so nested components no longer bypass their nested lifecycle.

`DesignSurface` owns its host from construction and exposes `CreateNestedContainer(owner)` plus `CreateNestedContainer(owner, containerName)`. Named nested containers inherit the owner's site services and publish names such as `toolStripContainer1.tools.namedComponent1`. The SDK smoke covers direct add/remove ordering and cleanup plus the named public API.

A concurrent SharpDevelop popup run also identified two distinct context-menu states. LibreWinForms now restores `ContextMenuStrip.Visible` after closing a stale WPF popup for the same strip before opening its replacement. Separately, SharpDevelop records the strip's `Opened` event even when its independently scheduled toolbar popup correctly closes the context menu before `Show(...)` returns from reentrant dispatcher work.

Validation used a coherent local package set rebuilt from ProGPU `895fe73` (`0.1.0-preview.6`):

```text
LibreWinForms SDK designer smoke       -> Success; directLifecycle=True, namedNested=True, namedNestedRemoved=True
LibreWinForms package lane             -> Success; packages, manifest, bundle, checksum
SharpDevelop fresh-cache rebuild       -> succeeds, 286 warnings, 0 errors
Focused FormsDesigner                  -> Success; 21 components, 54 rows, persistence true
Focused ContextMenuStrip               -> Opened, visibleAfterShow=True, 3 items
Broad ContextMenuStrip                 -> Opened, then correctly superseded by toolbar popup
Broad SharpDevelop workbench           -> all expected gates, exit code 0, balanced input teardown
```

Remaining designer work is visual selection/move/resize adorners, palette-originated drag data, grid/snap behavior, parent rules, extender-provider registration, undo/redo, verbs/menu commands, inherited components, localized resource round trips, and live event source navigation.

## 2026-07-10 toolbox creation and designer instances

LibreWinForms now ports the upstream managed `ToolboxItem` creation contract instead of exposing an empty stub. Type metadata, `ITypeResolutionService`, creating/created events, host-owned component construction, and `IComponentInitializer` default values all flow through standard typed APIs. `DesignSurface.ComponentContainer` exposes the real host container.

The portable host now creates and indexes designers before `ComponentAdded`, initializes them once, returns them from `IDesignerHost.GetDesigner(...)`, and disposes them before component removal. `TypeDescriptor` remains the standard extension path for attributed third-party designers; typed portable component/control/root designers cover source-owned controls while larger upstream designer source groups are ported. Root view selection and parent/location/size/text initialization now run through those designer contracts.

SharpDevelop's live FormsDesigner smoke creates a real `Button` with `ToolboxItem`, places it under the loaded `UserControl` using default values, verifies host registration plus `IComponentInitializer`, then destroys it and verifies site, parent, designer, and container cleanup. The sample source and dirty state are restored.

```text
LibreWinForms SDK designer smoke          -> toolboxCreation=True, attributedDesigner=True
LibreWinForms package lane                -> succeeds at 0.1.0-preview.sharpdevelop.1
SharpDevelop fresh-cache rebuild          -> succeeds, 286 warnings, 0 errors
Focused FormsDesigner                     -> toolboxCreated=True, toolboxRemoved=True, flushPersisted=True
Broad SharpDevelop workbench              -> all expected gates pass, exit code 0
```

ProGPU source stayed unchanged at `895fe73` (`0.1.0-preview.6`).

## 2026-07-10 interactive toolbox placement

LibreWinForms now routes a selected `IToolboxService` item through source-owned control, parent-control, and root designer input. Design-mode mouse down/move/up uses typed capture, translates child coordinates to the nearest parent designer, creates through one `DesignerTransaction`, supplies parent/location/size defaults to `ToolboxItem`, selects the result, and resets the selected tool exactly once. Pointer mode selects the clicked component, and the root designer implements the standard `IToolboxUser` contract.

The WPF host forwards mouse movement and captured mouse-up events and promotes hits on unsited composite-control implementation children to the nearest sited designer component. Runtime mouse handlers are suppressed for sited design-mode controls. No reflection, SharpDevelop component factory, or ProGPU source change was introduced.

The SDK smoke drives a `40,48` to `160,80` design drag and verifies a host-owned `Button` at `40,48`, size `120x32`, one transaction, selection, toolbox reset, runtime-handler suppression, and complete removal. SharpDevelop now performs its `24,32` to `144,60` toolbox check through the same selected-tool pointer path rather than calling `CreateComponents(...)` directly.

```text
LibreWinForms SDK designer smoke          -> Success; interactivePlacement=True
LibreWinForms package lane                -> packages, manifest, bundle, checksum succeed
SharpDevelop fresh-cache rebuild          -> succeeds, 286 warnings, 0 errors
Focused FormsDesigner                     -> toolboxCreated=True, toolboxRemoved=True, 54 rows
Broad SharpDevelop workbench              -> all expected gates pass, exit code 0
Native input lifetime                     -> 5 attaches, 5 detaches
```

Remaining work is visual selection/resize handles, moving and resizing existing controls, grid/snap lines, palette-originated drag data, parent rules, and command/undo integration.

## 2026-07-11 selection, manipulation, and managed undo

LibreWinForms now reuses the managed designer contracts for existing-control interaction. `PortableControlDesigner` selects a sited control, translates pointer coordinates to its parent, applies the system drag threshold, and supports move plus all eight edge/corner resize modes. Each committed drag owns one `DesignerTransaction` and emits typed `IComponentChangeService` changing/changed notifications for `Location` and/or `Size`; cancellation restores the original bounds. Runtime mouse handlers remain suppressed while the designer owns the pointer.

`WindowsFormsHost` consumes the standard `ISelectionService` and renders the primary selection border plus eight resize handles. The handles participate in hit testing and expose the expected resize cursors. Selection-service discovery is cached per child/load instead of recursively scanning a large hosted tree on every render, and root-host bounds intentionally ignore the root control's own `Left`/`Top` because the root is rendered at host origin.

The prior skeletal `UndoEngine` surface is replaced with a managed, reflection-free implementation based on the upstream transaction/event/undo-unit architecture. Property state uses typed `PropertyDescriptor` snapshots; component add/remove state records type, site name, parent, child index, and serializable property state. Added-component snapshots finalize when the enclosing transaction closes so `IComponentInitializer` parent/default values are included. Removal snapshots run while the control tree is intact, before `DestroyComponent(...)` detaches the parent. Undo and redo restore site, designer, parent, bounds, child order, selection, and rename/property state without private-field probing or ProGPU changes.

Validation used freshly packed `0.1.0-preview.sharpdevelop.1` packages from ProGPU `895fe73` (`v0.1.0-preview.6`):

```text
LibreWinForms SDK designer smoke       -> Success
Creation undo/redo                     -> site, parent, designer, location, size restored
Existing control move/resize           -> two transactions and exact change events
Move/resize undo/redo                   -> four transitions pass
Removal undo/redo                      -> parent, site, designer, bounds restored then removed
Reflection marker audit                -> clean for designer, undo, and WPF host sources
LibreWinForms package lane             -> 3 packages, docs, manifest, bundle, checksum
```

ProGPU remained clean and unchanged. Remaining designer parity is grid/snap behavior, palette-originated drag data, richer parent rules, extender providers, verbs/menu commands, inherited components, localized resources, and event-handler source navigation.
