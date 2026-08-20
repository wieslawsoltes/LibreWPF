# Linux rendering performance investigation

Date: 2026-08-20

## Summary

A large retained-mode WPF desktop workload was profiled on Linux because it remained slow after startup and consumed most of a two-vCPU virtual machine while apparently idle. The dominant observed activity was continuous native presentation through the ProGPU WPF window host. The renderer selected a CPU Vulkan adapter (`llvmpipe`) even though the virtual machine exposed accelerated OpenGL through `virgl`.

This change fixes one independently measurable waste: the native render pump now stops when either the managed window state or the actual Silk.NET native window state is minimized, and it also remains stopped for a host hidden through the managed API. In an equivalent three-run comparison, minimized process CPU fell from a median of 152.8% to 3.5%, a 97.7% reduction on a machine where 200% represents both vCPUs fully occupied.

The visible-window bottleneck is not claimed as fixed. Follow-up work now makes Linux backend discovery deterministic, fixes two EGL compatibility failures in the pinned native stack, rejects adapters below the renderer's feature floor, and adds a repeatable five-run windowed benchmark. The VM's accelerated `virgl` adapter is discoverable after those fixes, but exposes only OpenGL ES 3.0 / desktop OpenGL 4.0. ProGPU requires compute shaders and vertex/storage buffers, so automatic selection correctly retains CPU Vulkan on this machine. A virtual GPU with Vulkan support or at least OpenGL ES 3.1 / desktop OpenGL 4.3 remains necessary for accelerated rendering.

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
| Virtualization | Parallels virtual machine, approximately 4 GiB RAM; the original lifecycle profile used 2 vCPU and the 2026-08-20 GPU benchmark reported 4 logical processors |
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

An early unpatched experiment enabled both Vulkan and GL in the wgpu-native instance mask. Adapter selection still chose CPU Vulkan. A GL-only experiment then aborted in `wgpuInstanceCreateSurface` for the current X11/native-library combination, so that initial candidate was removed. The follow-up below addresses platform selection, context creation, feature qualification, and device ranking together rather than merely enabling another backend bit.

## Linux GPU backend follow-up

### Reproduction and capability inventory

The follow-up uses the repository's Release desktop sample rather than the private validation workload. It runs a 1280 by 800 window, disables presentation synchronization, warms up for 180 frames, measures 300 frames, and repeats the process five times. The benchmark records adapter identity, backend, type, driver, selection reason, average stage timings, and p50/p95/p99 frame tails.

The VM exposes two materially different graphics paths:

| API path | Adapter | Reported capability | Suitability |
| --- | --- | --- | --- |
| Vulkan | `llvmpipe (LLVM 20.1.2, 128 bits)` | CPU Vulkan device | Functionally compatible, not GPU accelerated |
| EGL/OpenGL | `virgl (Apple M3 Pro (Compat))` | EGL 1.5, OpenGL ES 3.0, desktop OpenGL 4.0 | Accelerated, but below ProGPU's feature floor |

Direct rendering is enabled and the accelerated EGL adapter works through X11, Wayland, GBM, and surfaceless probes. Its absence from the original WebGPU adapter list was therefore not caused by a missing DRM node or a completely disabled virtual GPU.

### Native EGL failures and fixes

The pinned native dependency is wgpu-native commit `33133da4ec5a0174cb21539ef2d3346f75200411`, which in turn pins wgpu commit `87576b72b37c6b78b41104eb25fc31893af94092`. Two behaviors in that historical wgpu EGL implementation prevented reliable use in this environment:

1. It always requested a robust EGL context when the extension was advertised. Mesa/virgl rejected that context combination with `EGL_BAD_MATCH`. The reviewed patch retries without the robustness attribute for `EGL_BAD_ATTRIBUTE`, `EGL_BAD_MATCH`, and `EGL_BAD_CONFIG`, matching the direction of later upstream fixes.
2. It preferred Wayland discovery whenever both Wayland and X11 were available, even when the actual application window was X11. The patch adds `WGPU_EGL_PLATFORM=x11|wayland`, and the managed window host sets it from the concrete native window handle before WebGPU initialization.

With those corrections and an automatic GLES version request, wgpu-native enumerates both CPU Vulkan and accelerated `virgl` OpenGL. That proves the compatibility patch restores discovery. It does not prove that the adapter can run ProGPU.

### Renderer feature gate

ProGPU's foundational pipelines use compute stages and vertex/storage buffers. On the VM's `virgl` adapter, wgpu reports `MaxStorageBuffersPerShaderStage=0`; compute shaders and vertex-stage storage are unavailable. Selecting it unconditionally produces invalid pipeline creation rather than a usable accelerated renderer.

Linux probing now requests GLES 3.1, enumerates Vulkan and OpenGL candidates, ranks discrete GPU above integrated GPU above unknown hardware above CPU, and prefers Vulkan only when candidates have the same acceleration class. The final instance is restricted to the selected backend because this pinned native version binds surface behavior to the instance's first backend. The VM's OpenGL adapter cannot initialize at the requested GLES 3.1 floor, so selection falls back to compatible CPU Vulkan. `WGPU_BACKEND=gl|opengl|vulkan` remains available for controlled diagnostics.

This is a correctness and portability improvement, not a visible-window performance improvement on the current VM.

### Windowed benchmark results

Raw results are under `external/ProGPU/artifacts/performance/linux-gpu-vm-20260820-tails/`. Each row is a separate warmed Release process using Silk.NET.WebGPU, the patched native library, automatic backend selection, 180 warm-up frames, 300 measured frames, and presentation synchronization disabled.

| Run | Wall FPS | Render avg | Present avg | Total avg | Total p50 | Total p95 | Total p99 | Frames over 16.67 ms |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 37.25 | 7.5618 ms | 16.7781 ms | 24.7293 ms | 21.7428 ms | 40.6428 ms | 73.9064 ms | 266 / 300 |
| 2 | 45.13 | 6.4815 ms | 14.0641 ms | 20.8275 ms | 18.0169 ms | 36.1568 ms | 78.9442 ms | 210 / 300 |
| 3 | 37.43 | 8.3042 ms | 16.0804 ms | 24.7814 ms | 20.2408 ms | 52.2035 ms | 84.0349 ms | 230 / 300 |
| 4 | 34.64 | 8.7309 ms | 17.7316 ms | 27.1140 ms | 20.9389 ms | 64.1284 ms | 87.7890 ms | 245 / 300 |
| 5 | 52.71 | 6.7931 ms | 10.6805 ms | 17.6911 ms | 16.6502 ms | 24.9048 ms | 37.3569 ms | 149 / 300 |
| Median | 37.43 | 7.5618 ms | 16.0804 ms | 24.7293 ms | 20.2408 ms | 40.6428 ms | 78.9442 ms | 230 / 300 |

Across runs, median render p95 is 28.9574 ms and median present p95 is 36.8560 ms. Presentation is the larger average stage, while both render and presentation have substantial tails. Because the selected Vulkan adapter is a CPU implementation, the `present` bucket includes software-driver, swapchain, XWayland, and compositor synchronization costs; it must not be interpreted as hardware-GPU execution time.

The median 24.7293 ms frame time misses the 16.667 ms target by 8.0623 ms, and the median run exceeds budget on 230 of 300 frames. The run-to-run FPS range of 34.64 to 52.71 also shows that a single VM run would be misleading.

### Implemented tooling

- `eng/build-wgpu-native-linux.sh` creates a pinned, Release, locked, reproducible linux-x64 or linux-arm64 native package with source commit, patch digest, ABI, toolchain, licenses, SONAME, exported-symbol, and file-checksum validation.
- `eng/run-linux-gpu-benchmark.sh` builds and runs the Release desktop workload repeatedly and preserves environment and per-run logs.
- `PROGPU_LINUX_GPU_BENCHMARK_WEBGPU_IMPLEMENTATION=dawn|silk|wgpu` selects the implementation under test; the desktop sample's existing Dawn default is unchanged outside the harness.
- Benchmark output now includes adapter diagnostics and percentile frame-stage measurements.
- The LibreWPF launcher automatically prepends a locally reviewed native build when one exists, keeping the unbuilt-source case explicit.

### Dawn qualification

Dawn is a viable WebGPU implementation in general, and ProGPU already has typed Dawn native-presentation support in its desktop and Avalonia paths. It is not currently a GPU-acceleration solution for this VM:

- The current Linux `DawnNativeWindowSource` requests Vulkan for both X11 and Wayland. Vulkan loader diagnostics from the running Dawn process show `libvulkan_lvp.so` and `llvmpipe`; Dawn therefore uses the same CPU Vulkan device here.
- Dawn itself supports native Vulkan and OpenGL backends, but using its OpenGL/GLES adapter requires a different adapter-discovery and surface path. The VM would still fail ProGPU's GLES 3.1 compute/storage feature floor.
- The WebGPUSharp 0.5.5 Linux ARM64 native binary is present, but depends on `libc++.so.1`, `libc++abi.so.1`, and `libunwind.so.1`; those libraries are neither installed on this Ubuntu image nor included in the application's runtime closure. Dawn launches only after supplying them locally.
- After local dependency resolution, the current Dawn/X11 bridge repeatedly reconfigures the surface (119 times during 120 measured frames), never reaches retained-scene reuse, reports internally contradictory timing, and can fault during a short-run shutdown. The resulting apparent 600-plus FPS is invalid and is not used as performance evidence.
- The WPF host currently creates a Silk-native context and directly calls the Silk `Wgpu` object for four surface operations. Selecting Dawn there requires an owned Dawn context factory, runtime packaging, and conversion of those calls to the backend-neutral `IWebGpuApi` path.

Consequently, switching the WPF host to Dawn now would replace one software Vulkan implementation with another while adding unresolved packaging, surface-lifecycle, measurement, and shutdown defects. Dawn should be qualified separately after its Linux runtime closure and surface state machine are fixed; it should not replace wgpu-native in this change.

### Required VM improvement

No source-level tuning can add compute/storage support to the current virtual GPU. A meaningful accelerated comparison requires changing the VM/host graphics path so one of these is true:

- `vulkaninfo --summary` reports a non-CPU Vulkan adapter; or
- EGL reports at least OpenGL ES 3.1, or desktop OpenGL 4.3 with the required extensions, and the WebGPU adapter exposes nonzero storage-buffer limits.

After upgrading the Parallels/host graphics stack or testing a VM technology that exposes those capabilities, rerun the same five-process benchmark and require all of the following before calling the result GPU accelerated:

1. `adapterType` is `DiscreteGpu` or `IntegratedGpu`, never `Cpu`.
2. The backend is the intended hardware Vulkan or compatible OpenGL path.
3. Device creation and foundational compute/render pipelines complete without validation errors.
4. Five-run medians and ranges improve under the same window size, page, warm-up, frame count, and synchronization setting.
5. Only after the accelerated adapter is confirmed, collect GPU timestamps or a native capture to split queue execution from presentation/compositor latency.

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

## Follow-up status and remaining work

Adapter diagnostics, accelerated-over-CPU ranking, a diagnostic backend override, EGL/X11 selection, EGL robustness fallback, and a repeatable retained desktop benchmark are implemented in the current follow-up. The remaining work is:

1. **Publish the reviewed native runtime.** The source build is reproducible, but normal consumers need signed linux-x64 and linux-arm64 runtime assets instead of a local artifact directory.
2. **Qualify real GPU adapters.** Run the benchmark matrix on hardware Vulkan and on GLES 3.1+ for both X11 and Wayland. Include virtio/virgl, Mesa hardware Vulkan, and at least one proprietary driver.
3. **Fix Dawn before considering a WPF switch.** Close the Linux C++ runtime dependencies, stop repeated suboptimal-surface reconfiguration, validate teardown, expose real adapter properties, and make the benchmark wait for meaningful completed frames. Then convert the WPF host's remaining direct Silk surface calls to `IWebGpuApi` and add explicit owned-context selection.
4. **Track occlusion beyond minimization.** If the window system can report full occlusion, suspend presentation while keeping dispatcher and input processing alive.
5. **Add native/GPU timing on capable hardware.** Correlate invalidation, replay, submit, queue completion, present, and compositor latency with frame sequence IDs and GPU timestamps.
6. **Add a compatibility renderer only if old virtual GPUs are a product requirement.** Supporting GLES 3.0 / GL 4.0 would require replacing compute and vertex-storage pipelines, not merely changing backend selection.

## Validation commands

The focused regression suite was run in Release on linux-arm64:

```bash
dotnet test src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj \
  -c Release --runtime linux-arm64 \
  --filter 'FullyQualifiedName~NativeRenderPumpStopsWhileWindowIsHiddenOrMinimized|FullyQualifiedName~NativeRenderPumpRemainsContinuousForExplicitFrameCallbacks|FullyQualifiedName~NativeRenderPumpStopsAfterStaticFrameUntilRenderIsRequested|FullyQualifiedName~NativeRenderPumpStopsAfterHostDisposal'
```

Result: 4 passed, 0 failed. The build emitted an existing assembly-conflict warning for `WindowsBase`; the changed projects compiled successfully.

The unfiltered project suite completed with 1,288 passed and 3 failed. The failures do not exercise the changed render-pump predicate: two source-inspection tests could not find repository files, and one separate source-text assertion did not match its target. These failures are recorded rather than hidden, but they are outside this performance change.

For the GPU follow-up on ProGPU preview 0.1.0-preview.53:

- Release desktop sample build: succeeded with 0 warnings and 0 errors.
- Linux backend-selection tests: 3 passed, 0 failed.
- Headless suite: 240 passed, 0 failed.
- Patched native build: completed from the exact pinned commits; ABI export, SONAME, manifest, license, and checksum checks passed.
- Silk/wgpu-native windowed benchmark: five independent runs completed; results are summarized above.
- Explicit `WGPU_BACKEND=gl` negative qualification: failed before surface creation with a managed `PlatformNotSupportedException` stating that no adapter met the renderer feature floor; automatic selection continued to complete on CPU Vulkan.
- Dawn smoke: native loading failed until local libc++ dependencies were supplied; after that it used `llvmpipe`, produced invalid benchmark state, and is recorded as an unsuccessful qualification rather than a pass.
- The full renderer suite was not completed during this follow-up. One older-base run reported an unrelated retained-recording allocation-budget failure. Current-base attempts grew to approximately 2.7–3.0 GiB RSS and destabilized this memory-constrained desktop session, so they were terminated and not retried. No full-suite pass is claimed.

## Limitations

- The external validation workload is closed-source and is not a repository benchmark.
- Native CPU stacks were unavailable under the host's `perf_event_paranoid` setting.
- GPU timestamps and capture tools were unavailable.
- The environment is a memory-constrained VM. Its CPU allocation changed between the original two-vCPU lifecycle profile and the four-logical-processor GPU follow-up, so results from the two phases are not cross-compared.
- No claim is made about startup, interaction latency, or visible-window throughput after the minimized-window fix.
- No accelerated before/after throughput claim is possible because this VM exposes no adapter meeting the renderer's feature floor.
- The Dawn numbers are deliberately excluded from comparisons because the bridge did not produce valid completed-frame measurements.

## Primary sources

- [Pinned wgpu source used by wgpu-native](https://github.com/gfx-rs/wgpu/commit/87576b72b37c6b78b41104eb25fc31893af94092)
- [Upstream wgpu robustness retry](https://github.com/gfx-rs/wgpu/commit/68a10a01d9dc210ce92cc7f3e40cdb7bccf60ba5)
- [Upstream wgpu `BadMatch` / `BadConfig` robustness correction](https://github.com/gfx-rs/wgpu/commit/e66813a88e351dbb66088b7cadd45c87f5328e0a)
- [Khronos OpenGL ES 3.1 specification registry](https://registry.khronos.org/OpenGL/specs/es/3.1/)
- [Khronos OpenGL specification registry](https://registry.khronos.org/OpenGL/index_gl.php)
- [Dawn project and supported native API families](https://dawn.googlesource.com/dawn.git/)
- [Dawn OpenGL/GLES adapter options](https://dawn.googlesource.com/dawn/+/refs/heads/main/docs/dawn/features/adapter_options.md)
