# Portable hyperlink pointer routing

The reported action is WPFGallery's NavigationWindow link from
`Views/Navigation/Page1.xaml` to `Page2.xaml` (issue #114). Its TextBlock owns the
presented drawing; the Hyperlink and its Run are content elements, not visuals.

## Source correction

`MouseDevice.LocalHitTest` consumes the portable host's selected pointer owner.
Previously, `PortablePresentationSource.TryHitTestOverride` stopped at its
UIElement, bypassing the `IContentHost.InputHitTest` promotion already used by
`UIElement.InputHitTest`. A selected TextBlock therefore received the pointer
instead of the hyperlink's text content.

The selected-owner path now reuses `UIElement.PromoteInputHit` at that same owner
and root point. It preserves content-host coordinate conversion, disabled-host
promotion, the handled-miss sentinel, and ordinary Windows HWND behavior. It
does not query owners again, walk drawing content after a handled native miss,
fabricate hyperlink rectangles, or change Hyperlink/navigation semantics.

## Bounded validation

- An isolated consumer compiled the unchanged WPFGallery Page1/Page2 XAML and
  code-behind. With layout processed between input events, the published
  preview.65 transport delivered a navigation request through ordinary input,
  but delivered none when the selected-owner callback returned the actual
  TextBlock. Both direct content hit testing and `UIElement.InputHitTest` still
  returned its Run. This paired control isolates the selected-owner boundary.
- Reusing those compiled sample resources with freshly built source Core and
  Framework assemblies, and the preview.65 rendering dependencies, delivered
  the same selected-owner move/down/up sequence to the Run, captured the
  Hyperlink, and raised exactly one request for `Page2.xaml`. The consumed Core
  and Framework hashes matched the isolated source build. This is source
  consumer evidence, not a newly packaged runtime closure.
- Four source tests execute the real mouse pipeline/content-host seam, transformed
  coordinates, one owner query, disabled-host promotion, and handled-miss
  rejection without drawing fallback. They use cross-platform UI threads and
  explicit source UIAutomation test dependencies. All four passed on macOS,
  without skips; the surrounding class passed 17 tests with four existing
  Windows-only STA skips.
- The SDK external consumer now includes a compiled XAML relative hyperlink in
  its existing NavigationWindow. It asserts typed move/down/up, hover, capture,
  one click/request, the destination page/text/URI, and back-journal availability.
  Existing direct Navigate/back/forward assertions remain independent. The
  generated consumer compiled locally; execution against newly produced SDK
  packages remains a required CI gate.
- The first package run exposed a fixture source lookup error: public
  `PresentationSource.FromVisual` returns the compatibility `HwndSource` facade,
  not the internal portable source. The fixture now obtains the active host
  through the existing typed diagnostics API, uses its actual portable source,
  and verifies that source owns the navigation window. All pointer, capture,
  navigation and journal assertions are retained. The corrected generator and
  generated external consumer compile locally; exact-head package execution
  remains required.

The deterministic callback in these contracts deliberately supplies a selected
owner. It does not qualify native owner selection, screenshot fidelity, actual
OS pointer injection into WPFGallery, or a published release. Those application
and exact-head package gates remain separate; this change alone does not close
the complete interaction report.
