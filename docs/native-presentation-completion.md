# Native frame completion before desktop capture

The original Gallery clipboard run [38484577502](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38484577502)
published frame 2 at 44.142 seconds and immediately failed its uniform-image
check. Separate unchanged-application diagnostics retained incomplete native
submission tokens after the source frame counter had advanced. The host calls
`SurfacePresent` before incrementing that counter; submission is not completion.
This establishes a missing capture-readiness condition, not a completed fix for
startup latency or the reported clipboard issue.

`ProGpuWpfDiagnostics.TryPollNativePresentationCheckpoint` explicitly polls the
live native compositor on its dispatcher owner thread. It requires the current
published frame, source scene/generation and device-recovery identity, then uses
the engine-owned submission token and its existing nonblocking completion query.
Busy, closing, disposed, recovering and unavailable hosts return no checkpoint.
Polling may retire completed native leases. It does not create a frame, wait,
capture memory, change source work or alter submitted-frame counters.

The Gallery wrapper publishes its capture request only after this checkpoint.
The receipt retains the exact frame, submission and recovery identity. The
capture driver rejects absent or mismatched completion metadata and still takes
one actual desktop capture per phase, enforcing every original pixel, foreground,
window-ownership, clipboard and physical-coordinate check. GPU completion does
not prove desktop scanout. The owner approved a preview69 release exception on
2026-10-14: a 90-second controller deadline and 87-second host reserve, retaining
the 55-second foreground setup bound. The original 60/57-second performance
target remains open in [LibreWPF #117](https://github.com/wieslawsoltes/LibreWPF/issues/117).
All original captures, exact retained RGB, clipboard actions, owned HWND/PID,
real completion, process exit and cleanup remain required. Acceptance uses no
diagnostic observer, and a failed run is not retried until it passes. Earlier
failed captures and traces remain failure evidence; approval is not qualification.
The pinned Microsoft source files, original image and action sequence are unchanged.

## Design sources and scope

- [wgpu queue](https://docs.rs/wgpu/latest/wgpu/struct.Queue.html#method.on_submitted_work_done)
  distinguishes submission, completed work and scheduled presentation. Adopt the
  existing real completion poll; do not infer completion from CPU counters.
- [Skia GrDirectContext](https://skia.googlesource.com/skia/+/refs/heads/main/include/gpu/ganesh/GrDirectContext.h)
  separates submission from asynchronous completion. [SkParagraph](https://skia.googlesource.com/skia/+/refs/heads/main/modules/skparagraph/include/Paragraph.h)
  keeps layout/painting separate. No layout or renderer cache is recreated here.
- [Direct2D Flush](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/nf-d2d1-id2d1rendertarget-flush),
  [DXGI Present](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present)
  [DirectWrite layout](https://learn.microsoft.com/en-us/windows/win32/directwrite/text-formatting-and-layout),
  and [Win2D swap chains](https://learn.microsoft.com/en-us/windows/apps/develop/win2d/using-win2d-without-built-in-controls)
  separate drawing and presentation. Preserve actual desktop pixel verification
  after queue completion instead of equating either stage with visible output.
- [WebRender](https://docs.rs/webrender/latest/webrender/struct.Renderer.html)
  retains pipeline epochs separately from asynchronous screenshot handles.
  Preserve source-frame identity; do not replace it with a timer or retry capture.
- [Vello](https://docs.rs/vello/latest/vello/struct.Renderer.html) submits a retained
  scene through wgpu; [Parley](https://docs.rs/parley/latest/parley/) owns text
  layout independently. The checkpoint observes existing work rather than
  rebuilding scene, layout, glyphs or device resources.
- [HarfBuzz](https://harfbuzz.github.io/shaping-and-shape-plans.html) returns shaped
  glyph data. Shaping completion supplies no GPU or desktop-completion evidence.

This additive diagnostic does not redesign startup/lazy initialization, retained
scene or shaping reuse, visibility culling, glyph/path/texture cache identity or
eviction, uploads, worker preparation, GPU batching, DPI/subpixel/hinting, fallback
fonts or variation state. Device recovery invalidates the observed frame. Its
managed cost is fixed storage/work plus the existing native completion poll;
native retirement remains proportional to completed owned submissions.
Offline rejection controls do not establish Windows desktop qualification.
