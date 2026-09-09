# Native MIL Windows SDK activation connection

## Acceptance and boundary

Acceptance application: the existing complete package-mode MVP. User action:
start the application, construct its text/vector/image resources and show its
first native-rendered window. The blocking source path was the SDK bootstrap's
blanket Windows rejection followed by a separate unconditional Windows return.
Removing only the rejection would leave native selection without portable
window activation. This batch connects the existing source/host implementation;
it does not replace the acceptance application or add another renderer.

Windows x64 and ARM64 processes now enter the same native SDK startup branch as
the existing Unix desktop path. Native process admission rejects architectures
other than x64/ARM64 before media selection: the Windows x86 transport payload
does not imply a ProGPU native MIL x86 backend. Missing native binaries and
devices remain failures, not permission to fall back to Windows MIL or managed
portable replay. Ordinary managed Windows SDK startup keeps its existing return
and Windows MIL behavior.

## Source-backed prerequisite trace

The previous source and ProGPU batches implement the following paths. This
review is implementation evidence, not executed Windows behavior.

| Boundary | Existing implementation retained by SDK activation |
| --- | --- |
| Pre-application media selection | `ProGpuWpfNativeMediaServices.Initialize` selects `PortableWpfRuntime` and installs lazy geometry/text/document defaults before the explicit source module initialization and before the generated Application.Main constructs the application. It creates no GPU/window. |
| Composition ownership | MediaSystem, ChannelManager and MediaContextNotificationWindow select the frozen portable transport before native MIL ownership. The selection survives shutdown. |
| Primary input / text services | InputManager selects PortableKeyboardDevice/PortableMouseDevice from the same frozen policy. TextServicesManager and InputMethod focus/enable handling avoid WPF TSF/HWND ownership for portable input. Committed host text is supported; host composition/IME preferences remain an explicit unsupported contract. |
| Source startup | PresentationFramework's module initializer publishes the typed registrar. The SDK registers an explicit NativeMilWgpu host factory and throws if registration fails. Application.EnsureHwndSource and Application.Run choose registered portable window ownership. |
| Show / hidden source | Window.CreateSourceWindow selects TryCreatePortableWindow before HwndSource creation. The activation service rejects failed registration callbacks and missing hidden-source capability; stable source identity precedes SourceInitialized. |
| Common media | TextFormatter's typed provider-first path, geometry bounds/hits/combine and BitmapSource storage routing use portable media ownership rather than the OS. Native Windows font discovery and legitimate OS services remain intact. |
| DPI / desktop / pointer units | ProGpuWpfWindowHost publishes PortableDesktopTransform from actual client/content-scale policy and uses that same policy for native pointer normalization. Popup geometry updates remain parent-before-child. Native popup owner transport scale only decodes placement; the separately surfaced popup owns its framebuffer DPI. |
| Popup ownership | WpfPortableNativePopupHost inherits the owner renderer, shares the live owner host, initializes hidden, configures native ownership before Show and disposes rejected hosts. Separate surfaces are excluded from owner replay; owner-surface overlays retain the existing canonical placement path. |
| Native first frame | RenderNativeMilFrame requires the typed session/root, compiles canonical source state, binds external-image leases and presents through the native compositor. Missing native session state throws. Full-surface/uniform-DPI profile guards remain. |

No source OS checks were mechanically removed. The SDK change is the final
registration connection for these existing prerequisites, not proof that every
Windows source API is portable. General geometry utilities not needed by this
startup path, advanced presentation, outgoing text drags and full IME composition
remain separate explicit work; they are not reported complete. Application
closure and final qualification remain required before a supported release.

## Authored regression and build work

The existing startup contract fixture now checks that the Windows early return
is managed-only, native selection precedes registration, x64/ARM64 admission is
explicit and failed registration still throws. The MVP's existing self-test
checks the requested renderer against frozen portable source media. Its live
path checks a presented host for a native MIL session frame on the host thread;
a managed presentation cannot satisfy that check.

Before editing the bootstrap, the full MVP source at `96d6a1d63` was copied into
`artifacts/native-core-mvp-build.Bb1vvF`. A standalone parent props/targets pair
replaces repository build infrastructure, not application source. The original
sample's source/XAML/resources/project remain present. NuGet configuration maps
ProGPU/LibreWPF IDs only to the newly produced development feed and uses a fresh
local package cache. NativeMilWgpu package-mode compilation completed with
0 warnings/errors in 8.54 seconds. No application or self-test executed.

The updated source-contract project compiled with 116 existing warnings and
0 errors in 12.31 seconds. The revised SDK package was produced successfully
into a new feed at `artifacts/native-core-sdk-activation.i6P0lf`, preserving the
earlier feed and SDK. The other 22 selected packages were copied unchanged.
SDK production rebuilt PresentationBuildTasks net10.0/net472 and retained normal
pack-time checks. No ProGPU implementation changes were needed for this SDK
connection; it consumes the existing shared native/typed implementation.

The updated complete MVP, including the new validation code, was compiled against
that feed with a second fresh `packages-activation-final` cache. The sample's
compiled native flag, rather than the mutable runtime diagnostic record, enables
its native assertions. A missing/mismatched record then fails explicitly.

| Build-only target | Result | Elapsed |
| --- | --- | --- |
| NativeMilWgpu / osx-arm64 | 0 warnings, 0 errors | 5.51 s |
| NativeMilWgpu / win-x64 | 0 warnings, 0 errors | 3.75 s |
| NativeMilWgpu / win-arm64 | 0 warnings, 0 errors | 4.20 s |
| ManagedPortable / win-x64 | 0 warnings, 0 errors | 3.40 s |

Each uses the pinned host SDK, `dotnet build`, Release, `-m:1`,
`UseSharedCompilation=false`, explicit `ProGpuWpfRendererMode` and
`RuntimeIdentifier`, `AppendRuntimeIdentifierToOutputPath=true` and the fresh
RestorePackagesPath. The standalone parent props force package reference mode.
Managed output has a separate BaseOutputPath so it does not replace native
binaries. The serialized loop stops on failure and exited 0. These Windows RID
builds ran on macOS; they are not Windows guest execution evidence.

The isolated build location contains implementation snapshots and is preserved,
not a release bundle. Intermediate consumer builds also succeeded before the
final compile-flag assertion and SDK README update; the table above records the
final source/package compile pass, not those earlier results.

No tests, application/renderer runs, benchmarks, Windows VM graphics workloads
or CI polling are part of this implementation batch. Final qualification must
rebuild and consume exact delivery artifacts, exercise supported Windows native
startup and mixed-DPI/input/popup behavior, compare renderers and pass required
CI. The implementation-stage Windows admission is not a qualification result.
