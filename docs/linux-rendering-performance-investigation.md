# Linux rendering performance investigation

Date: 2026-08-18

## Summary

A large retained-mode WPF desktop workload was profiled on Linux because it remained slow after startup and consumed most of a two-vCPU virtual machine while apparently idle. The dominant observed activity was continuous native presentation through the ProGPU WPF window host. The renderer selected a CPU Vulkan adapter (`llvmpipe`) even though the virtual machine exposed accelerated OpenGL through `virgl`.

This change fixes one independently measurable waste: the native render pump now stops when either the managed window state or the actual Silk.NET native window state is minimized, and it also remains stopped for a host hidden through the managed API. In an equivalent three-run comparison, minimized process CPU fell from a median of 152.8% to 3.5%, a 97.7% reduction on a machine where 200% represents both vCPUs fully occupied.

The visible-window bottleneck is not claimed as fixed. A compatible accelerated Linux WebGPU backend remains the highest-value follow-up.

## Symptom and workload

The validation workload is an external, private Release-build desktop application used only to exercise LibreWPF. It opens a large repository and presents a 1000 by 600 logical-pixel window at 2.0 scale, producing a 2000 by 1200 render surface. Repository data finished loading quickly, but high CPU use and sluggish interaction continued.

The repeatable scenarios were:

1. Launch the Release workload with external network calls disabled for determinism.
2. Wait for the main window and repository data to settle.
3. Leave the visible window untouched for an idle sample.
4. Minimize it through the X11 window manager and verify `_NET_WM_STATE_HIDDEN`.
5. Collect three consecutive 10-second `pidstat` windows for the original and patched assemblies.

The private executable and its proprietary symbols are not part of this repository. Raw diagnostic artifacts are retained outside the repository; this document contains only aggregated framework-relevant findings.

## Test environment

| Component | Value |
| --- | --- |
| OS | Ubuntu 24.04.4 LTS, Linux 7.0.0-29-generic, arm64 |
| Virtualization | Parallels virtual machine, 2 vCPU, approximately 4 GiB RAM |
| Desktop | GNOME Wayland session with XWayland/X11 application window |
| Display | 3600 by 2016 at approximately 60 Hz, scale factor 2 |
| .NET | SDK 10.0.400, runtime 10.0.11, linux-arm64 |
| GPU device | virtio GPU, PCI `1af4:1050`, Parallels subsystem `1ab8:0010` |
| Vulkan adapter | `llvmpipe (LLVM 20.1.2, 128 bits)`, CPU device |
| OpenGL adapter | `virgl (Apple M3 Pro (Compat))`, direct rendering enabled |

Memory pressure was significant during some runs, including active swap. CPU comparisons therefore use paired samples from the same session. RSS observations are descriptive and are not treated as evidence of a memory regression or improvement.

## Collection

The investigation followed an evidence-first .NET performance workflow:

- `pidstat -u -r -p <PID> 1 10` for per-process CPU, faults, and RSS;
- `dotnet-counters monitor --process-id <PID>` for allocation, GC pause, thread-pool queue, and contention signals;
- five independent `dotnet-stack report --process-id <PID>` snapshots;
- `dotnet-trace collect --process-id <PID> --profile dotnet-common,dotnet-sampled-thread-time` followed by `dotnet-trace report ... topN` and Speedscope conversion;
- framework trace switches for native-loop and render-surface state;
- `vulkaninfo --summary` and `glxinfo -B` for adapter ownership;
- `xprop` to verify the minimized native-window state.

Native Linux `perf` sampling was unavailable because `kernel.perf_event_paranoid=4` and the run had no elevated privileges. No RenderDoc or GPU timestamp instrumentation was available. Managed EventPipe data, repeated stack snapshots, backend logs, and OS counters were combined instead of treating any single source as conclusive.

## Evidence

### Persistent CPU saturation

An initial 15-second idle probe averaged 149.1% process CPU. A separate controlled 20-second baseline averaged 137.3% CPU: 136.0% user time and 1.4% system time. Later short samples varied with VM memory pressure, but the process repeatedly occupied most of both vCPUs without input.

Managed runtime counters commonly showed about 10.9 MB/s allocation and short GC pauses, usually 3 to 22 ms per second. The thread-pool queue remained near zero and lock contention was absent. GC, thread-pool starvation, and managed locking therefore did not explain the sustained CPU saturation.

### Presentation-path ownership

All five independent managed stack snapshots caught the UI thread in the same path:

```text
ProGpuWpfWindowHost.RunPortableNativeLoop
  -> ProGpuWpfWindowHost.DoEvents
    -> ProGpuWpfWindowHost.OnRender
      -> ProGpuWpfWindowHost.Present
```

Other application background threads were waiting in those snapshots. Framework surface tracing showed repeated full presentations at 2000 by 1200 pixels. Native-loop tracing and source inspection showed that a pending render kept the owner loop on its active 1 ms delay instead of the 16 ms idle delay.

In the sampled-thread-time trace, the inclusive all-thread wall-time percentages were approximately:

| Stack | Inclusive | Exclusive |
| --- | ---: | ---: |
| `OnRender` / `DoEvents` | 7.93% | — |
| `Present` | 7.71% | 3.66% |
| `Compositor.RenderScene` | 4.06% | — |
| `WgpuContext.Submit` | — | 3.92% |
| retained WPF replay | approximately 0.18% | — |

These percentages use an all-thread wall-time denominator that includes waiting threads, so they must not be read as direct CPU percentages. Their value is attribution: rendering and submission dominate the active UI-thread samples, while retained-tree replay is comparatively small.

### Rendering backend

Backend diagnostics selected:

```text
Adapter 'llvmpipe (LLVM 20.1.2, 128 bits)', backend=Vulkan
```

`vulkaninfo` confirmed that this was the only Vulkan adapter and classified it as a CPU device. `glxinfo` independently showed an accelerated `virgl` OpenGL renderer. Thus the workload was continuously submitting full-window frames to a software Vulkan implementation.

An experimental change enabled both Vulkan and GL in the wgpu-native instance mask. Adapter selection still chose CPU Vulkan. A GL-only experiment then aborted in `wgpuInstanceCreateSurface` for the current X11/native-library combination. That candidate was removed: enabling a backend is not sufficient unless surface creation is compatible and adapter selection can rank devices safely.

## Implemented fix

`ProGpuWpfWindowHost.ShouldPumpNativeRender` now rejects rendering when any of these conditions is true:

- the host is disposed or native close has started;
- the host was hidden through the managed API;
- the managed WPF window state is minimized;
- the actual Silk.NET native window state is minimized.

Checking both managed and native state matters because the window manager can minimize an X11 window without the action originating in the managed `WindowState` setter. A pending render request is left intact, so restoring or showing the window can produce the required next frame.

Focused tests cover hidden and managed-minimized hosts and retain the existing assertions for static-frame coalescing, explicit callbacks, and disposal.

## Equivalent before/after validation

Both builds used the same Release workload, data state, machine, window size, DPI, software Vulkan adapter, X11 minimize operation, three-second post-minimize settling period, and three consecutive 10-second `pidstat` samples. `_NET_WM_STATE_HIDDEN` was verified before collection.

| Build | Run 1 CPU | Run 2 CPU | Run 3 CPU | Median | Range |
| --- | ---: | ---: | ---: | ---: | ---: |
| Original | 153.25% | 151.40% | 152.80% | 152.80% | 151.40–153.25% |
| Patched | 3.70% | 3.40% | 3.50% | 3.50% | 3.40–3.70% |

The median reduction is 149.3 percentage points, or 97.7%. The remaining approximately 3.5% includes the native event loop and non-rendering application background work.

The visible workload still consumed high CPU after this patch. No visible-window speedup is claimed.

## Ownership and dominant cause

The current ownership chain is:

1. The application/framework invalidates or requests frames continuously.
2. `ProGpuWpfWindowHost` pumps and presents those frames even when the native window is minimized.
3. wgpu-native selects the only Vulkan adapter.
4. That adapter is `llvmpipe`, so rendering and submission consume host CPU rather than an accelerated GPU path.

The minimized-window waste belongs in the WPF host and is fixed here. The larger visible-window cost spans ProGPU backend discovery, wgpu-native surface compatibility, adapter ranking, and the virtual GPU driver stack.

## Proposed follow-ups

1. **Expose adapter type and selection diagnostics.** Record backend, adapter type, driver, device IDs, surface compatibility, and the reason an adapter won. Current name/backend logging is insufficient for automated policy.
2. **Rank compatible hardware adapters over CPU adapters.** Enumerate candidates only after confirming surface compatibility. Prefer accelerated Vulkan normally, then a compatible accelerated GL/GLES path, and use CPU Vulkan only as a declared fallback.
3. **Upgrade and validate the native WebGPU stack.** The current GL-only X11 surface path aborts inside the pinned native library. Test a newer wgpu-native release against X11, Wayland, virtio/virgl, Mesa hardware Vulkan, and proprietary drivers before enabling GL fallback.
4. **Add a backend override for diagnostics.** A documented ProGPU option should select or exclude backends without relying on ambient environment variables. Invalid combinations should fail with a managed diagnostic rather than a native panic.
5. **Track occlusion beyond minimization.** If the window system can report full occlusion, suspend presentation while keeping dispatcher and input processing alive.
6. **Instrument frame production.** Add EventSource markers for invalidation, render-pump decision, replay, submit, present, and dropped/coalesced frames. Include frame sequence IDs so input-to-present and invalidation-to-present latency can be correlated.
7. **Create representative benchmarks.** Add a retained desktop scene with high DPI, text, scrolling, popups, and a controlled animation. Report frame time, process CPU, allocation rate, replay count, submit time, and present count for visible, idle, minimized, and restored states.

## Validation commands

The focused regression suite was run in Release on linux-arm64:

```bash
dotnet test src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj \
  -c Release --runtime linux-arm64 \
  --filter 'FullyQualifiedName~NativeRenderPumpStopsWhileWindowIsHiddenOrMinimized|FullyQualifiedName~NativeRenderPumpRemainsContinuousForExplicitFrameCallbacks|FullyQualifiedName~NativeRenderPumpStopsAfterStaticFrameUntilRenderIsRequested|FullyQualifiedName~NativeRenderPumpStopsAfterHostDisposal'
```

Result: 4 passed, 0 failed. The build emitted an existing assembly-conflict warning for `WindowsBase`; the changed projects compiled successfully.

The unfiltered project suite completed with 1,288 passed and 3 failed. The failures do not exercise the changed render-pump predicate: two source-inspection tests could not find repository files, and one separate source-text assertion did not match its target. These failures are recorded rather than hidden, but they are outside this performance change.

## Limitations

- The external validation workload is closed-source and is not a repository benchmark.
- Native CPU stacks were unavailable under the host's `perf_event_paranoid` setting.
- GPU timestamps and capture tools were unavailable.
- The environment is a memory-constrained two-vCPU virtual machine; absolute visible-window numbers should not be generalized to physical Linux systems.
- No claim is made about startup, interaction latency, or visible-window throughput after the minimized-window fix.
- The adapter fallback experiment was deliberately removed after failing surface compatibility validation.
