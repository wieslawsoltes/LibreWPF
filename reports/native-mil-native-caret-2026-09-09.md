# Native MIL source editing caret — 2026-09-09

## Acceptance action and source-backed blocker

Acceptance: focus and type in the existing Showcase TextBox/RichTextBox, preserving
source caret drawing and Windows host ownership. CaretElement.Win32CreateCaret
did not create a caret for PortablePresentationSource, but Win32SetCaretPos still
ran for that source on Windows and could retry/throw without owning a Win32 caret.
This was identified by source tracing, not reproduced runtime failure. The current
Windows SDK guard remains necessary; this batch closes one concrete source branch.

## Implementation and authored coverage

- ProGPU `bd5ff978` owns NativeWindowCaret and source-generated Win32 calls for a hidden,
  bitmap-free OS mirror on a real local native window. Queue/editor identity,
  thread affinity, shape reuse, different-HWND replacement, stale release,
  failure reporting and reentrancy protection are explicit.
- The neutral IPortableNativeCaretHost/IPortableNativeCaretService contract
  keeps WPF source types and fake presentation handles out of backend APIs.
- Source CaretElement selects portable ownership before legacy Win32 calls.
  It publishes insertion geometry through actual root placement and source device
  mapping, retaining normal caret/blink/bidi/italic/interim drawing. Optional
  mirror failure remains observable and releases any mirror owned by that caret;
  it never falls through to source Win32 synchronization or another renderer.
- Focus deactivation, hide, adorner migration/detach and service replacement
  release the old identity. The host releases its mirror before marking itself
  disposed or destroying its native window; failed native release remains an
  explicit retryable ownership failure rather than stranded disposal state.
- The bridge preserves explicit service priority and new same-host registrations
  during old bridge cleanup. Renderer/device-target renewal does not replace the
  live native window or its caret domain. Both renderer modes share this path;
  no C++ MIL/rendering implementation change is applicable to OS queue metadata.
- Ten ProGPU ownership fixtures plus the existing Windows native-input test
  class cover the state machine and actual native caret owner/rectangle/hidden
  state. Source document fixtures exercise real CaretElement placement at DPI 2,
  movement, rejection, inactive selection, detach and absent-service routing.
  Bridge fixtures cover fake-handle rejection, service priority and rebinding.

Source and bridge work adds no product reflection, second caret renderer, GDI
bitmap, pixel readback or WPF-local platform P/Invoke. Native fixed ownership work
is O(1) and allocation-free after construction; it is not independent-lane SIMD
compute. This is not a measured performance improvement.

## Build-only record

All commands use `build --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'`:

- Standalone ProGPU SDK 10.0.201, `src/ProGPU.Tests/ProGPU.Tests.csproj`:
  final native ownership/Windows fixture compilation 0 warnings, 0 errors,
  28.06 seconds.
- Root pinned SDK, `src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationFramework.Tests/PresentationFramework.Tests.csproj`:
  2 warnings, 0 errors, 5.45 seconds after fixing three field naming analyzer
  errors by using properties; no rule was disabled.
- Root pinned SDK, `src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj`:
  initial bridge/source graph build 116 warnings, 0 errors, 18.34 seconds;
  final bridge lifecycle fixtures 21 warnings, 0 errors, 8.81 seconds.
- Root source Application.Run harness initially compiled with 4 warnings,
  0 errors in 57.05 seconds; final rebuild 0 warnings, 0 errors, 22.40 seconds.

No tests, verifiers, applications, VM/GPU workloads, benchmarks or CI checks ran.
Latest fetched ProGPU main `102e39e5088b462624da6296ff70a43ed2c5d8b4` remains an
ancestor of this work. Unrelated native edits and performance-artifact deletions
were preserved. This batch does not change the native MIL coverage source digest.

## Remaining requirements

Native Windows execution and visible Showcase/Toolkit editing, actual focus changes,
mixed-DPI client coordinates, magnifier/accessibility behavior and retained
native/managed output remain final qualification requirements. Windows cannot
be admitted to native SDK package mode on this checkpoint alone; its payloads,
remaining application integration and the suspended VM blocker remain open.

Windows provides no generation identity for another library replacing a caret on
the same HWND; such sharing requires host coordination and is not claimed safe.
Cocoa/Linux native IME/accessibility mirrors and general DirectX/Direct2D/COM/Win2D
completeness are not implemented by this optional Windows metadata service.

See the [ProGPU contract and primary-source provenance](../external/ProGPU/docs/native-mil-native-caret.md)
and [active core finish queue](../docs/native-mil-core-delivery.md).
