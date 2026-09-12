# Multi-window rendering on Linux

Opening a second top-level `Window` on a headless X server with Mesa software rendering aborted
the process inside wgpu's GLES/EGL backend
([issue #102](https://github.com/wieslawsoltes/LibreWPF/issues/102)):

```
thread '<unnamed>' panicked at wgpu-hal/src/gles/egl.rs:300:14:
called `Result::unwrap()` on an `Err` value: BadAccess
   12: wgpuInstanceCreateSurface
thread caused non-unwinding panic. aborting.
```

An EGL context is thread-affine, and wgpu's GLES backend binds and unbinds one around its work,
`unwrap()`ing the result. Two things in LibreWPF put a foreign context in the way.

**A GLFW client context left current.** Transparent-framebuffer windows
(`AllowsTransparency="True"`, and the native popups created for one) ask GLFW for a client API so
its X11 backend picks a visual with an alpha channel. GLFW makes that context current on the
creating thread; WebGPU never uses it, but `create_surface` releases whatever context the thread
holds and Mesa answers `EGL_BAD_ACCESS` because the bound context is GLFW's. `ProGpuWpfWindowHost`
now drops it on window load, before any surface exists. This is what AvalonDock hit when its
drop-target overlay appeared during a docking drag.

**A WebGPU instance per window.** Requesting an adapter enumerates every backend, GLES included,
even where wgpu ends up selecting Vulkan, and `enumerate_adapters` makes the GLES context current -
colliding with the contexts other live instances hold. Native popup hosts always borrowed their
owner window's device; top-level windows now do the same through a process-wide render device, so
one instance serves every window. Set `PROGPU_WPF_DISABLE_RENDER_DEVICE_SHARING=1` to opt out (it
is off on Windows, which presents through D3D12). Sharing is best-effort: if the owner tears its
device down first, the next window retires it and creates one itself, and any window still using
the device can hand it on.

## Still broken: rendering through the GLES/EGL backend

The fixes above make multi-window work where wgpu **selects Vulkan**. Where it selects GLES/EGL to
render through, a second window still aborts, because that backend rebinds its EGL context around
every device operation with no coordination between windows. It reproduces whether the windows
share a device or not, so the LibreWPF host cannot arrange around it - it needs a fix in wgpu or in
ProGPU's use of it. **On Linux, render through Vulkan.** With lavapipe installed, AvalonDock's
57-test DevFlow suite goes from 57 failures with the process aborted to 56 passing.

## Selecting a backend

LibreWPF has no knob for this, and neither does the environment: wgpu-native reads its backend mask
from the `WGPUInstanceExtras` chained onto the instance descriptor and does **not** consult
`WGPU_BACKEND`. That mask is built in ProGPU's `WgpuContext`, outside this repository.

Adapter selection is not neutral either - with both llvmpipe and lavapipe installed,
`PowerPreference.HighPerformance` prefers the OpenGL adapter, because lavapipe reports itself as a
CPU adapter. Hiding one driver is the only way to steer it from outside today: force GL/EGL with
`VK_DRIVER_FILES=/nonexistent/none.json`, or force Vulkan by installing only a Vulkan ICD. Because
the choice is neither configurable nor predictable, report the selected backend when diagnosing a
run - `ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host)` then
`host.CompositionTarget.Context.AdapterName` and `.AdapterBackendType`.

## Test coverage

`src/ProGPU.Wpf.MultiWindowSmokeHarness` opens three top-level windows with transparent secondaries,
presents frames on all of them, closes the render device owner and opens another, asserting that
one device serves them all. `eng/progpu-wpf-linux-multi-window-smoke.sh` runs it under `Xvfb` with
Mesa software rendering, and the `Linux headless multi-window render device smoke` job in
`.github/workflows/progpu-wpf-sdk.yml` runs that in CI.
