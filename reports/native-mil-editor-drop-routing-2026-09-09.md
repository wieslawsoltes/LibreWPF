# Native MIL editor drop and menu routing — 2026-09-09

## Acceptance action and source-backed blocker

Acceptance: drag text into the existing Showcase TextBox/RichTextBox, focus the accepted
drop target, and open its editor context menu within the actual client surface.
Source tracing found that TextEditorDragDrop queried IsWindowEnabled against a
WindowInteropHelper handle on Windows even for a portable window. Portable source
identities are not Win32 HWNDs. Its foreground request did not reach the portable
host; editor context-menu client clipping also selected by OS instead of source.
These are source-backed findings, not reproduced runtime failures.

## Implementation

- Drop admission uses PopupControlService.UsesNativeWindowing before Windows
  queries. A portable view must have a live presentation source, enabled root and
  editor, and actual-owner modal admission. Unhosted, disabled, read-only and
  modal-blocked drops return None, preserving the source text for rejected moves.
- Accepted drops request activation through the existing typed Window host
  callback. The existing cycle-rejecting popup input-owner traversal finds the
  actual owner; placement targets and synthetic HWNDs do not select it. Hidden,
  disposed, disabled, unowned and modal-blocked sources cannot activate a window.
  Host rejection does not fabricate IsActive or fall through to Windows APIs.
  Actual activation events remain authoritative. The existing editor Focus call
  follows the activation request; rejection does not undo an already accepted paste.
- Client clipping shares the existing source-root/local-coordinate implementation
  on all portable sources, including public HwndSource proxies. Framebuffer DPI
  is not applied to source DIPs. Native Windows MIL retains GetClientRect and its
  device transform. ContextMenu/Popup continue to own desktop placement.
- Authored source regressions exercise real TextEditor drag-enter admission with
  retained FlowDocument layout, rejected move effects, typed activation callbacks,
  source/owner lifetime and proxy/root clipping at framebuffer scale two. Existing
  source-graph guards require ownership selection before legacy Windows calls.

This is WPF-specific adaptation of existing shared ProGPU services; no new C++
renderer, platform API, shader or WPF-local native call is needed. No product
reflection was added. Owner traversal is allocation-free O(popup depth) with
dependent links; clipping/admission are fixed-size scalar control work, not an
independent-lane CPU compute fallback. No performance claim follows.

## Build-only record

Root pinned SDK, serialized `build --no-restore -m:1 -v:quiet
'-clp:ErrorsOnly;Summary'`:

- PresentationFramework.Tests: final 2 warnings, 0 errors, 9.18 seconds. The first
  compilation found an inaccessible DragEventArgs constructor in the fixture;
  the fixture now uses public ProcessPortableDragDrop and the real routed event,
  without reflection or a new test-only factory. It asserts handler delivery so
  an early ingress rejection cannot masquerade as editor admission coverage.
- ProGPU.Wpf.Tests: 116 warnings, 0 errors, 19.93 seconds.
- ProGPU.Wpf.RealApplicationRunHarness: 0 warnings, 0 errors, 8.39 seconds.

No tests, verifiers, applications, VM/GPU workloads, benchmarks or CI checks are
executed in this implementation batch.
Freshly fetched ProGPU main `102e39e5088b462624da6296ff70a43ed2c5d8b4` remains
an ancestor of the feature branch. This source-only batch leaves its existing
`bd5ff978` integration and unrelated native/performance-artifact edits unchanged.

## Remaining qualification

Source inspection also confirms an implementation gap outside this ingress fix:
portable DragDrop.DoDragDrop still returns None instead of starting a source drag.
This checkpoint must not be described as working editor-to-editor drag-and-drop.
Its source-drag host contract remains an implementation task for that acceptance
action, not a failure that is merely waiting for testing. The default
SilkNetWpfDragDropService currently subscribes only to FileDrop; it does not yet
provide OS text-drag negotiation/transport. Typed/injected text ingress is not
evidence that dragging text from an external application works by default.

Actual drop transport, paste payload fidelity, move-source deletion, popup-owned
editors, focus/activation, keyboard and mouse context menus and mixed-DPI visible
placement must run in the final application gate. These fixtures do not prove
native accessibility/IME, cross-process drag parity or package startup. Windows
SDK admission remains guarded; Windows payloads and the VM resume dependency are
still open. Broader DirectX/Direct2D/COM/Win2D completeness remains in the full
goal backlog, not a claim made by this routing change.

See the [active delivery queue](../docs/native-mil-core-delivery.md).
