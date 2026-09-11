# Native external application render-cost investigation

## Validation-mode correction

The original diagnostic launch enabled `PROGPU_WPF_EXTERNAL_RUN_VALIDATE=1`,
not `PROGPU_WPF_EXTERNAL_LIVE_VALIDATE=1`. Its required terminal marker was
`External SDK Application.Run validation succeeded.`, emitted after the queued
normal-priority application-run checks and shutdown. Earlier references below
to that process awaiting live-input success were incorrect: the live-input task
is enabled only by the separate LIVE switch. Neither marker was observed, so
application-run completion remains unqualified, but absence of the live marker
does not itself diagnose that run. Preserve these modes separately in subsequent
reproductions and invoke the actual live-input gate when qualifying input.

Acceptance remains the unchanged external SDK application's stable presentation,
geometry and live input validation. The sampled DrawingBrush fix is ProGPU
`748097a5`; this diagnostic process uses locally substituted binaries, not final
package evidence. No validation timeout, input admission or renderer selection was
relaxed.

## Live evidence

The same process (PID 5806, execution session 27669) remained live across checks.
Its output has neither a new exception nor the required success marker. Do not
restart it merely because an observation interval expires.

Two independent two-second native process samples show changing render work:

- First: render-pass completion, Metal command-buffer allocation semaphore waits,
  picture-mask preparation and WGSL parsing.
- Second: 1,333 of 1,480 main-thread samples in the final native submission path;
  1,329 in device polling, principally Metal completion waits. Other samples
  remained in render-pass completion.

These observations contradict a fixed dispatcher-only deadlock, but do not prove
frame completion or establish which GPU workload dominates. Samples are retained
under `artifacts/native-exact-b54db165.dBPf7B/` as
`external-drawing-brush-sample.txt` and `external-drawing-brush-sample2.txt`.

## Source trace and next bounded work

`create_semantic_picture_binding` creates a fresh child engine for each picture
preparation, renders its scene, adds its submission count to the parent, then
destroys the child. `create_child_engine` calls `create_engine`, which creates the
shared vector pipeline and initial vertex buffer. The RGBA picture-image path
has a separate retained backing cache; this does not establish equivalent reuse
for picture masks. New guideline masks therefore expose a repeated setup cost
worth measuring, not yet a proven sole cause of this application's delay.

Before implementing reuse, measure preparation count/time versus GPU submission
time. Any bounded child-engine reuse must preserve owner-thread/device/format
identity, nested picture isolation, external image rebinding, exact scene updates,
submission accounting and device-loss teardown. It must not retain stale borrowed
views or relax native source input. Do not substitute a full-frame rectangle,
disable masks, or switch to CPU rendering to make validation finish.

Final package, platform/VM, pixel/performance and latest-head CI gates remain open.

## Corrected-head compilation checkpoint

ProGPU `280485fb` compiles both native providers for macOS x64 using the existing
x86_64 CMake configuration. The build completed successfully before CTest was
started; the rebuilt native MIL test passed under translation in 2.43 seconds.
Build output: `artifacts/native-exact-b54db165.dBPf7B/build-x64-280485fb.log`.
This is compilation and a MIL contract test, not x64 GPU/pixel qualification.
GCC and Linux ARM64 CI remained in progress at the subsequent check.

The same external application process was confirmed live after 15 minutes,
without validation output. No restarted process or timeout extension replaces
that observation. Application completion and acceptable render latency remain
unproven despite the passing compilation/contract checks.

## Interrupted capture and diagnostic reconstruction

The managed stack collected with installed `dotnet-stack` 9.0.661903 connects
the main thread to `ProGpuWpfWindowHost.PresentNativeMil`, `RenderNativeMilFrame`,
`OnRender`, `DoEvents` and `RunPortableNativeLoop`; sampled worker threads are
idle. Raw output is `external-drawing-brush-managed-stack.txt` beside the native
samples. These are diagnostic Debug-application observations, not optimized
before/after performance measurements.

On Xcode 26.4.1/macOS 26.6, a 15-second attached Metal System Trace started but
did not produce an exportable document: table-of-contents export later failed
with `Document Missing Template Error`. Both recorder PID 8545 and application
PID 5806 subsequently disappeared, with no live handles or app success marker.
The trace must not be used as GPU execution evidence. The temporary application
directory also no longer exists; its disappearance has not been attributed.

The normal external harness refused reconstruction because the local
LibreWPF.ProGPU package differed from the repository Release assembly. Preserve
that check. The explicit SDK `--build-packages-only` lane is rebuilding the
diagnostic WPF packages using the existing b54db165 ProGPU/canonical dependency
snapshot. Its output is unqualified and must not be presented as final-head
package provenance. The four mirrored WPF source modifications were compared
byte-for-byte with the root checkout and match. Reconstruction output is in
`package-reconstruction.log`; the rejected attempt is in
`external-reconstruction.log`. Once rebuilt, use the existing native-loop trace
switch to distinguish incomplete frames from repeated presentation. Final clean
dependency-head packages and the full release gates remain separate work.

Package reconstruction completed successfully through the build-only lane. The
normal external harness subsequently passed package provenance checks, recreated
the temporary application, and launched its RUN validation child. That harness
captures output until child termination; an empty parent log is not evidence
that the child emitted nothing. Its current diagnostic native library is the
older dependency snapshot, not the updated standalone ARM64 build (SHA-256 values
`c4c5f193a30bfaae49bc40107498fd9093a5baa16313a81968f5ad4dd4fa00cd` and
`bac2f45a12e3d85ef91c01eac06c50403bb690cd1a0d31e7e8c325c7e6201717`, respectively).
Do not qualify the latest native implementation from this reconstruction run.
