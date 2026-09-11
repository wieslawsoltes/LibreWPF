# Native external application render-cost investigation

## Repeatable external application compilation

The external application's explicit `**/*.cs` item now honors MSBuild's
`DefaultItemExcludes` and `DefaultExcludesInProjectFolder`. Previously a second
build picked up generated `obj` assembly attributes and failed with CS0579,
requiring diagnostic intermediate directories to be moved aside. The project
shape assertions now require these exclusions; application/source assertions
and SDK selection remain unchanged.

Two consecutive native-selected builds completed with zero errors without
removing or moving either project's intermediate directory. Logs:
`external-incremental-first.log` (156 warnings, 2.53 s) and
`external-incremental-second.log` (312 warnings, 3.14 s), under the diagnostic
artifact directory below. Existing nullable-context warnings remain. The second
build also compiles the shared IME-boundary helper and both RUN/LIVE call sites.
These are diagnostic dependency-snapshot builds, not final-head package or
application runtime qualification. Cocoa popup admission remains the next blocker.

## Next blocker: Cocoa native popup ownership

The diagnostic application now explicitly expects the unsupported-composition
exception before switching the compiled metadata specimen to DoNotCare for the
separate ordinary-focus contract. The harness applies this boundary in RUN and
LIVE validation independently. This is an acceptance-scope correction consistent
with the delivery plan's existing IME deferral, not an implementation of IME
preferences or removal of the product rejection guard.

The native diagnostic build succeeds and RUN exits 1 at
`ValidatePopupOpeningAfterRun` with `The selected native popup owner could not be
configured.` Access-key, class-command and keyboard-navigation checks preceding
that call returned successfully. Evidence is `external-ime-build.log` and
`external-ime-run.log` under `artifacts/native-exact-b54db165.dBPf7B`.

A standalone AppKit probe creates a visible owner and hidden child:
`before: visible=false registered=true`; after `addChildWindow(..., .above)`:
`after: visible=true parent=true`. This contradicts the shared Cocoa provider's
assumption that adding a child preserves its hidden state. The `.out` ordering
alternative was also tested and throws NSInvalidArgumentException; it is not an
admitted ordering mode. No product native-owner check has been relaxed.

Apple's [child ordering contract](https://developer.apple.com/documentation/appkit/nswindow/addchildwindow(_:ordered:))
admits above/below. Its [parent contract](https://developer.apple.com/documentation/appkit/nswindow/parent)
forbids assigning the parent directly outside subclass implementation and explains
that ordering a child out detaches it. A correct repair therefore needs an explicit
prepare/show ownership design; blindly hiding after attachment or directly assigning
the parent is not acceptable. The existing hidden-before-show rule must be reconciled
with native attachment before implementation. Broader modal popup support remains
separate. This evidence is macOS diagnostic input, not final package qualification.

## Resolved immediate failure

The explicitly selected native diagnostic run completed 40 presentations before
exiting with the secondary `run validated before exit` assertion. Progress
markers in the queued RUN callback then proved it entered validation and completed
the initial window-property dispatcher drain. A fail-fast wrapper around that
test callback exposed the original exception and exited with code 1:
`PlatformNotSupportedException: Portable input-method preferences require the
host composition contract.` The stack is `ValidateAccessKeyRoutingAfterRun` →
access-key processing → keyboard focus → `InputMethod.GotKeyboardFocus`.

The access label targets `ExternalValidationTextBox`, whose XAML explicitly
requests IME On, Native/FullShape conversion and Automatic sentence mode. These
are not default no-preference focus settings. The source guard deliberately
rejects them under portable input until a typed host composition contract exists.
Do not remove that guard or report ordinary committed-character delivery as IME
parity. The application is not stuck permanently in first-frame rendering.

The harness now logs RUN entry and the first dispatcher checkpoint and reports
the original queued validation exception before exiting nonzero. No assertion
or success marker is relaxed. The marked and fail-fast diagnostic logs are
`external-marked-run.log` and `external-failfast-run.log`. The generated external
application rebuilt successfully with native selection and native hit testing.
Final-head package, live-input, platform and CI qualification remain open.

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
