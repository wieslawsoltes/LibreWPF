# Native external application render-cost investigation

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
