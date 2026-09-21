# Windows native MIL text runtime admission — 2026-09-15

## Finding

The merged Windows ARM64 package run reported the same five text lines and
metrics as stock WPF, but a visual follow-up found that its text fixture had no
`LibreWPF.RequestedRendererMode` entry in `runtimeconfig.json`. Its live process
loaded the packaged WPF and ProGPU managed assemblies but not `progpu_native.dll`;
the test-window crop of the stock and purported-native screenshots was
pixel-identical. Those observations do **not** qualify native-MIL text parity.
They do not contradict the separate Showcase result: its runtime configuration
did request `NativeMilWgpu`, and its displayed self-test checks a presented native
MIL session frame.

The guest's global `librewpf.sdk/0.1.0-preview.45` cache contained older targets
(59,308 bytes, dated 2026-08-22) with no native-mode define or requested-renderer
option. The exact SDK package from the coordinated 23-package closure contains
newer targets (77,342 bytes, SHA-256
`4246791af69057158f7f26aae23d8d09bc86821c8541e758f639e6f750f73ff6`).
`RestorePackagesPath` did not govern early NuGet MSBuild SDK resolution. Merely
unpacking the exact SDK into `NUGET_PACKAGES` did not suffice: without its NuGet
cache metadata the resolver replaced it with the stale same-version package.

## Admission change

`eng/progpu-wpf-windows-native-mil-showcase.ps1` now installs the exact SDK
archive into a unique private NuGet cache, with its SHA-512 content record and
`.nupkg.metadata`, before any application build. The NuGet SDK resolver uses
that cache for the whole package gate. Both built executables must record
`NativeMilWgpu` in their runtime configuration. The text fixture additionally
checks its live ProGPU window host and reports `TEXT_RENDERER NativeMilWgpu`;
the differential gate requires that marker before comparing metrics. An old SDK,
managed portable renderer, or Windows MIL fallback therefore fails closed.

The merged LibreWPF Build run
[34932393529](https://github.com/wieslawsoltes/LibreWPF/actions/runs/34932393529)
finished successfully, but predates this admission change. Its green text
differential is not native-MIL visual qualification. The Windows ARM64 package
gate on this branch passed before reinstating the text result as bounded
native-vs-stock layout evidence. The native Windows x64 hosted gate still has to
pass at the PR head. Pixel/typographic inspection and broader
application/platform parity remain separate final gates.

## Validation provenance

An initial VM execution used a September 14 package folder whose
`PresentationFramework.dll` hash did not match the merged package closure; its
pre-display SystemCommands failure is not evidence about the current merge.
The follow-up then downloaded the exact 23-package artifact from successful
merge-commit Build `34932393529`. Its SDK targets hash is
`8999c30f7a73973d4b7833ca2c633a9f5ae8cd1ebf8bf638b94f791a63ef2fb6`;
the ARM64 `PresentationFramework.dll` package member hash is
`f499df1dce8d49ae0f28fe6c1eaeb9b69076783d6ab07af25bb78f648a526756`.
The merged-package Showcase built with zero warnings/errors and its pre-display
object self-test printed its success marker. Its displayed `Application.Run`
self-test also passed startup, system commands, controls, secondary window,
editor, document, and shutdown. The stock-WPF text fixture and the exact-SDK
native-MIL text fixture both built without warnings/errors and reported the
same five line starts (`0,32,58,88,121`). The native text process printed
`TEXT_RENDERER NativeMilWgpu`; stock WPF did not. Actual guest metrics were:

```text
stock WPF:  width=211.333 height=93.100 font=14.000 tops=0.000,18.620,37.240,55.860,74.480
native MIL: width=211.333 height=93.105 font=14.000 tops=0.000,18.621,37.242,55.863,74.484
```

This is a real source-layout differential after native host admission, not a
claim that all text ink, typography, sizing, languages, or documents match.

The VM has Windows PowerShell 5 rather than PowerShell 7. In a focused child
process probe, `Start-Process -PassThru` followed by a manually timed
`WaitForExit` left `ExitCode` blank even for `where.exe` exiting zero; directly
owning `System.Diagnostics.Process` retained exit code `0`. The package gate
now uses direct asynchronous stdout/stderr capture, bounded waits, retained
exit codes, and private logs. This compatibility correction does not weaken
the CI exit-code or success-marker checks. The guest runs the unsigned shared
test script with a per-process execution-policy allowance only; no persistent
VM policy or production wrapper is changed.
