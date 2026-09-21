# Toolkit floating editor native input — 2026-09-14

## Acceptance action and source path

`ProGPU.Wpf.ToolkitApp` floats its AvalonDock editor, clicks into the editor on
the separately presented native host, verifies source focus and the host's own
GPU device index, then redocks. The same live gate continues through anchorable
hide/show, auto-hide groups, layout replacement and serialization.

The first diagnostic stalled while looking up `Window.GetWindow(EditorTextBox)`.
AvalonDock's inherited logical WindowService still names MainWindow; the actual
`PresentationSource.FromVisual(EditorTextBox).RootVisual` is its floating window.
The native trace showed the floating host was already created and presenting.
The live gate now uses the actual source root for floating and redocked identity,
while retaining owner, distinct host, frame, input and host-removal checks.

The next diagnostic clicked and focused the editor but observed an unuploaded
native index after a later render wakeup. An index check moved into the same
receiving-host click query, before input dispatch can invalidate its generation.
That exposed a second issue: the cache diagnostic getter itself refreshed a
secondary host, installing another generation after the successful GPU query.
`ProGpuWpfWindowHost.TryGetGpuHitTestCacheSnapshot` now reads native metadata for
the installed scene without refreshing it. Managed cache diagnostics retain
their existing refresh behavior. No managed hit index is used as evidence for
native input.

## Evidence and limits

The diagnostic source graph built the native-mode Toolkit with no warnings or
errors. `toolkit-diagnostic-floating-index-query.log` reached the floating editor
and reported `NativeIndex=True, DeviceIndex=False, Primitives=66, Owners=40`
after the diagnostic getter refreshed the scene. With the corrected getter,
`toolkit-diagnostic-floating-index-nonrefresh.log` ends with
`ProGPU WPF Toolkit live input validation succeeded` after all required actions.
The focused source guard test `ToolkitFloatingInputUsesActualPresentationSource`
passes. ProGPU's native CTest set passes 20/20, including its process-exit
synchronization regression. ProGPU's current `54adc6a0` native build-only lane
also stages both `osx-arm64` and `osx-x64` payloads with both providers and SDK
libraries; native CTest passes 20/20 on each architecture (x64 under Rosetta).
The staged payloads remain unqualified package inputs. All seven
`ProGpuWpfNativeHitTestingTests`, including
`HostQueriesUsePresentedNativeOwnersAndNativeDiagnostics`, pass after staging
matching source assemblies and native libraries in a separate test output.
The source guard runs from the repository output so its file lookup
resolves correctly. The combined copied-output attempt was not counted because
that source guard could not locate the repository from the external directory.

These diagnostics combine source-built LibreWPF assemblies with current ProGPU
artifacts and a prior SDK package. They are not final package, independent CI,
Windows VM, macOS/Linux system or distribution evidence. The ProGPU process
mutex regression had a separate before/after 134/0 reproducer, but Toolkit's
successful diagnostic teardown alone does not qualify all shutdown lifetimes.
Keep all package, platform and exact-head CI gates before advancing pins/merging.
