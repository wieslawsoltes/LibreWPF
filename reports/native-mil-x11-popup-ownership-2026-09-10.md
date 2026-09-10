# Native X11 popup ownership connection

Acceptance applications: the existing MVP and Toolkit/AvalonDock package gates.
User actions: open a native menu, ComboBox dropdown or tooltip on Linux/X11,
retain source desktop placement and actual native ownership before Show.

The source trace found a concrete failure in WPF's native popup setup: an OR of
independent Xlib results could admit a partially configured popup. Configuration
now belongs to ProGPU's shared `NativePopupWindow` provider. It requires hidden
top-level same-display/root admission and confirmed owner, override-redirect and
ordered menu type properties. WPF adapts only the actual native handles/display;
its owner-configuration failure propagation is preserved. The same cleanup now
also covers exceptions from hidden native/target initialization and native Show;
those paths previously could retain a partially initialized popup host. Rejection
never selects another surface kind. Source guards cover the cleanup boundary.

The [ProGPU contract](../external/ProGPU/docs/native-mil-x11-popup-ownership.md)
records the architecture, original source provenance, borrowed native lifetimes,
primary specifications, paired-renderer applicability, CPU-cost analysis and
authored regressions. Root graph fixtures now reject the old local Xlib popup
setup rather than requiring its source text.

ProGPU's Linux build/test workflow adds an explicit real-X-server fixture under
Xvfb with TRX output. The requested lane cannot skip a missing DISPLAY. This is
an additional server-state contract, not a substitute for package-mode live
popup/render/input and Windows comparisons. Automatic CI remains enabled; no
checks were polled or manually executed before feature freeze.

The investigation also confirmed the independent Cocoa modality dependency:
the current GLFW popup host does not provide an NSPanel surface. The existing
AppKit session guard remains; no class rewriting, modal-poll bypass or surface
fallback was introduced. Linux native dialog suppression remains separate too.

The first ProGPU test-graph compilation found SYSLIB1051 in the new fixture's
cross-assembly attribute record import. The fixture now passes its unmanaged
record by pointer, without changing assembly-wide marshalling. The corrected
graph compiled with 0 warnings / 0 errors in 17.10 seconds; a subsequent build
completed with 0 warnings / 0 errors in 1:14.75. The final ProGPU test graph,
including protocol-width rejection and the non-racing mapped-window fixture,
compiled with 0 warnings / 0 errors in 26.70 seconds.
The WPF bridge/test graph compiled with 116 warnings / 0 errors in 32.65 seconds;
no warnings were suppressed. This includes the source-level popup cleanup and
shared-provider contract guards, not execution of them.
No native payload
was rebuilt or restaged: this is shared host coordination, not a change to C++
renderer binaries. Updated backend/bridge packages still need production and
exact-head consumption during final qualification. This is not feature freeze,
core delivery, broad DirectX parity or a claim that either PR's CI is green.

During this batch `origin/main` advanced to
`73cda9a5243e3bea75e0c8c2fc4d4ecdaf44889d` (CAD/rendering merge, 286 commits not
yet in the feature branch). The preceding compile results are on the feature
branch before that integration, not latest-main evidence. Upstream also changes
`progpu_native_semantic_render_execution.cpp`, one of the unrelated dirty native
files in the local submodule. Preserve that work; perform merge preparation in
the clean native checkout before updating delivery inputs. No clean/stash/reset
of the user's native edits or deleted performance artifacts was performed.

Popup commits are pushed: ProGPU `549f1b55`, LibreWPF `7ded04a11`. Merge
preparation is now active in
`artifacts/native-core-build.KvxVug/progpu`, detached at `549f1b55`, with
`73cda9a5` as MERGE_HEAD. Git reported 22 conflicted paths spanning the shared
native header/generated contract, 3D/retained rendering and brush builders,
shaders, tests, package verification and CI. No conflict was resolved by taking
one implementation wholesale. Resolve the source contracts first, regenerate
the managed contract, preserve both feature sets, then compile the combined
tree. The current native build directories still describe the earlier unmerged
renderer; they are not upstream-integration evidence. The active original
submodule remains at the pushed popup commit with unrelated local work intact.
