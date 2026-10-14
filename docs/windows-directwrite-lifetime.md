# Windows DirectWrite module lifetime

PresentationCore retains its original `NativeLibrary.Load` reference to
`dwrite.dll` and its factory entry point until the operating system terminates
the process. It does not unload the module from `AppDomain.ProcessExit`.
Retained native font objects still contain code and vtable pointers into that
module. Managed font ownership alone cannot keep an explicitly unloaded DLL
mapped, and process-exit callbacks can run while those objects remain usable.

This fixes the shutdown boundary exposed by
[issue 278](https://github.com/wieslawsoltes/LibreWPF/issues/278). The original
Showcase printed successful completion, then a live render thread failed while
exporting a native GlyphRun. Its `Environment.Exit` call ran on the validation
worker. The old PresentationCore exit handler cleared the factory pointer and
freed DirectWrite without retiring those font callers.

The [paired lifetime diagnostic](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38382846473)
used unchanged, fully qualified `c1ca1004d0d8a44e0ed527168e5e514390884879`
product DLLs and three isolated processes:

| Original glyph export | DirectWrite at callback | Result |
| --- | --- | --- |
| Before process exit | Original module loaded | Exact snapshot, exit 0 |
| In a later process-exit callback | Module already unloaded | Native access violation, `0xC0000005` |
| Same exit callback with a diagnostic module pin | Original module loaded | Exact snapshot, exit 0 |

The failing case retained a validated first-chance native dump and a stack
through `FontFace.GetDesignGlyphMetrics` and `TryGetPortableNativeGlyphRun`.
All product bytes stayed unchanged. The loader, initializer, GlyphRun,
GlyphTypeface and native font wrapper sources are identical between the original
failing `f9507dc67920d9e72af64f35c8455b59e70ed87f` and this diagnostic producer.
Artifact `11804681491` has SHA256
`327bdaa8aec3af159e3f63efdc161ef343cc3f20ed52f9929e6d090f8fed8d84`.
The original Showcase failure had no exception dump, so this does not identify
its precise faulting native instruction.

The product fix retains the existing loader reference; it adds no pinning call,
per-font library load, renderer substitution or shutdown-time font workaround.
[FreeLibrary's lifetime contract](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-freelibrary)
and [process termination behavior](https://learn.microsoft.com/en-us/dotnet/api/system.environment.exit?view=net-10.0)
are the relevant platform boundaries.

`eng/WindowsDirectWriteExitConsumer` checks an ordinary export and an uncached
export in the actual process-exit event. It only borrows the module handle, never
pins or loads another reference. The Windows x64 and ARM64 package Showcase
lanes run it in a separate process after their original application and text
checks, require the final exit-callback success marker, and verify the exact
package implementation and native files. Original Showcase assertions, clocks,
inputs and captures are unchanged. Complete fixed-product CI, original
application acceptance and public-package validation remain required.
