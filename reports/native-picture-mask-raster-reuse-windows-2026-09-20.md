# Native picture-mask raster reuse on Windows ARM64

Date: 2026-09-20; updated 2026-09-21

This record qualifies the retained native picture-mask raster change against
the live `ProGPU.Wpf.ToolkitApp` and AvalonDock input sequence on Windows 11
ARM64 under Parallels. It intentionally separates the pre-change observation
from the exact replacement-artifact result. A running application, a compiled
fixture, or a green package job alone does not qualify the optimization.

## Pre-change observation

The observed application used the native ProGPU runtime produced from merged
ProGPU commit `9f9ee4b3` and the LibreWPF Toolkit source overlay used by the
integration gate. The test ran in the signed-in guest desktop with
`PROGPU_NATIVE_TRACE_PICTURE_MASK=1`; it was not launched as `SYSTEM`.
The loaded `progpu_native.dll` SHA-256 was
`83AD5972E41404537EF4D0EE2CEF85FF94F9E409F8D7A477094E321901ADD74D`.
The companion `ProGPU.Backend.Native.dll` SHA-256 was
`B34C2B949AC85CF65405B0E4A2859A8BAD43BE0018991AB7B775EE9E3C34A3EB`.

The application reached these live validation stages:

- initial frame and geometry publication at 967 by 605 logical DIPs,
  1934 by 1210 pixels, and device scale 2;
- transient-surface quiescence;
- filter focus and filter text;
- popup input;
- AvalonDock document and anchorable menus;
- editor interaction.

During those interactions the parent scene advanced through at least generation
11. The same 816-byte nested picture-mask scene was rasterized repeatedly across
parent generations. Representative child render durations were:

| Parent generation | Source pixels | Child render time |
| ---: | ---: | ---: |
| 2 | 1934 by 1210 | 27,770.331 ms |
| 3 | 1934 by 1210 | 51,977.826 ms |
| 4 | 1934 by 1210 | 57,064.704 ms |
| 5 | 1934 by 1210 | 71,854.153 ms |
| 7 | 1648 by 545 | 65,378.799 ms |
| 10 | 1934 by 1210 | 30,521.970 ms |
| 11 | 1934 by 1210 | 35,963.336 ms |

The descriptors also repeated at 1648 by 416, 1648 by 462, 1648 by 503,
1648 by 545, and smaller menu surfaces. This is direct evidence that rebuilding
the parent semantic bundle caused unchanged nested mask content to be rerendered;
it is not a general GPU throughput measurement.

The baseline process was still alive while this record was written. It is not
counted as a completed application pass, and its source-overlay payload is not
an exact final-package qualification.

## Replacement implementation

[ProGPU PR #173](https://github.com/wieslawsoltes/ProGPU/pull/173), merged as
`910571ae18c5b8ec7f1fe41908fd8aa2304844e0`, retains
eligible picture-mask raster textures in a bounded cache independent of
incremental picture images. Reuse is keyed by the complete nested scene, raster
descriptor, engine flags, device, and format. External image bindings and seeded
captures used by the nested stream remain ineligible; unrelated parent
bindings are outside its identity. Sampling transforms, opacity, guidelines,
and span uniforms remain parent-scene state rather than part of the retained
raster.

The merged implementation owns one owner-thread-affine scratch child engine per
parent engine, preserves exact nested-scene identity across repeated snapshots,
accounts the child inventory recursively without double-counting borrowed
handles, and propagates device loss into the owned child before parent teardown.
LibreWPF now pins the merge commit above. The successful Windows ARM64 renderer
artifact from that exact commit must still be installed coherently and used for
the post-change run before the integration pull request leaves draft state.

## First replacement artifact result

ProGPU Build `35514942684` successfully produced the Windows ARM64 runtime for
the earlier branch head `beca3d86cae9ed13e399ecce83ceb4b4f02de1b0`. The
downloaded and guest-installed `progpu_native.dll` SHA-256 was
`9BEAB1E689921F187F0CE47767F1BDEB9B52CF1C10F80B5FA6B7EA7E031A4015`.
The original baseline DLL was retained separately before replacement.

The replacement run reached the live frame, geometry, transient-surface, and
filter-focus stages. Generation 2 correctly populated nine distinct retained
picture-mask raster descriptors for the same 816-byte nested scene. The first
two descriptors revisited in generation 3 nevertheless reported `cacheHit=0`
and rerendered for 29,261.383 ms and 29,020.112 ms. The nine-entry working set
fit below the 64 MiB byte budget, but the implementation still had an
independent eight-entry FIFO ceiling. Sequential traversal therefore evicted
the next descriptor before reuse and thrashed the complete working set.

That run was stopped after the repeat misses proved the artifact did not meet
the cache-reuse gate; it is not an application pass. ProGPU head `8dc588f8`
raises the still-bounded entry ceiling to 64 while preserving the 64 MiB byte
budget, and adds a Direct2D/WebGPU regression that fills nine descriptors and
requires a one-submission hit when the first is revisited after an outer-scene
generation change. Local AppleClang validation passes all 19 native CTests.

## Second replacement artifact result

ProGPU Build `35517055892` produced the exact Windows ARM64 runtime for
`8dc588f8523253b373abe5c17db4cb028de5aa08`. The downloaded artifact was
`progpu-native-runtime-win-arm64`; `progpu_native.dll` was an ARM64 PE32+
binary with SHA-256
`D45C3207D396D1252503849FF60E547DD7309A3E2C78AADC54D3B32ADBFB65A3`.
The rejected first replacement remained preserved separately before this
binary was installed.

Generation 2 populated all nine Toolkit picture masks. Representative first
renders included 28,560.857 ms at 1934 by 1210, 28,635.887 ms at 961 by 238,
and 40,000.068 ms at 1648 by 503. Generation 3 still reported `cacheHit=0`
for the first 1934 by 1210 stream (27,813.841 ms) and the otherwise unique
961 by 238 stream (30,644.548 ms). The unique descriptor miss disproved a
remaining entry-count or same-descriptor-only explanation.

The retained mask rasters shared `semantic_picture_cache` with incremental
picture images. Image insertion removed every cached entry with the same nested
scene id, including mask-only entries, so later parent generations found no
mask working set. Mask retention also replaced a different nested stream when
it shared one scene id and raster descriptor. That run was stopped after the
two generation-3 misses proved failure; it is not an application pass.

ProGPU head `2eaf23bf` separates bounded mask and incremental-image caches,
deduplicates masks only when their complete nested scene is equivalent, keeps
both caches in native memory inventory and teardown, and restores the original
eight-entry image-history bound independently of the 64-entry/64 MiB mask
working set. New Direct2D/WebGPU regressions cover an image insertion between a
mask seed and revisit plus two different nested scenes sharing one descriptor.
The focused test and all 19 native CTests pass locally.

## Third replacement artifact result

ProGPU Build `35519234209` produced the exact Windows ARM64 runtime for
`2eaf23bf170d27fcafc79c00f250f95300b6cb24`. The ARM64
`progpu_native.dll` SHA-256 was
`E52CFB308E60E592EF355F46E60189DCAC697CEBA38CB5F3D35B980E6DDC4C57`.
Both previous replacements and the original baseline remained preserved before
installation.

Generation 2 again populated all nine masks, including separate same-size
1934 by 1210 streams. Generation 3 nevertheless rerasterized the first full
surface for 32,526.979 ms and the unique 961 by 238 surface for 30,701.513 ms.
The separated caches were therefore working as designed but were never
eligible for lookup in the live application: the parent engine carried
external-image bindings for unrelated Toolkit content, and the blanket
nonempty-table check disabled every mask lookup and retention even though the
816-byte nested mask streams did not reference those images. This run was
stopped after the two repeat misses and is not an application pass.

ProGPU head `3239b455` now captures the complete ordered external-image
resource, generation, role, view and extent identity with each retained raster.
Stable unrelated parent bindings permit reuse; any identity change forces a
miss, while direct nested external-image scenes remain rejected by the existing
append-only identity contract. The same-descriptor regression now runs with a
stable unrelated external binding and still requires a one-submission revisit.
The focused regression and all 19 native CTests pass locally.

## Fourth replacement artifact result

ProGPU Build `35521446303` produced the exact Windows ARM64 runtime for
`3239b4555e4ed3e47eadfb65aef61aebe82aab4a`. The downloaded artifact supplied
three ARM64 PE32+ DLLs. Their SHA-256 values were:

- `progpu_native.dll`:
  `09E1C39EE92C55CAADE625EA2A32A12D3C2E8078B55CCDF3C7894A6BE9396E58`;
- `progpu_native_dawn.dll`:
  `2FA4AEB68DAE55006DA4F633000456767F86D2391A04DF5324B92EF828C67FAF`;
- `progpu_native_direct2d.dll`:
  `77E3F99E7F3CCE41A8061A96E8CF4C1312A7F2FA903A235319588E7E28B9B62B`.

All three were installed together after preserving the rejected runtime set.
Generation 2 populated the nine expected descriptors. The first 1934 by 1210
generation-3 revisit nevertheless reported `cacheHit=0` and rerasterized for
28,080.133 ms. This run was stopped at that decisive miss and is not an
application pass.

The complete parent external-image identity was still too broad: Toolkit
advanced an unrelated sideband binding between generations even though the
retained 816-byte mask stream did not consume it. The corrected admission now
walks picture images and picture/composite masks recursively and rejects any
nested external-image dependency. Once admitted, unrelated parent binding
changes cannot affect the raster and are excluded from its key. Incremental
picture-image history retains its complete sideband identity comparison.

The Direct2D/WebGPU regression now changes an unrelated binding between the
seed and one-submission revisit. A separate nested external-image case requires
two child submissions and remains fail-closed. The focused Metal regression
and all 19 locally configured native CTests pass. A new exact Windows ARM64
artifact and completed Toolkit/AvalonDock run remain required.

## Current-source MSVC preflight result

To avoid waiting for another full CI cycle before checking the preceding root
cause, the Windows guest built ProGPU `cfe9a7fe` directly with MSVC 19.44 for
ARM64. The resulting `progpu_native.dll` SHA-256 was
`FE8FA85966E78C9D290C8702AD7C3342B7BDC827B221E59C4CEE2BC1B585DCCF`.
This is a source-build preflight, not the final exact CI artifact.

The run populated all nine generation-2 descriptors. Its first 1934 by 1210
generation-3 revisit still reported `cacheHit=0` and rerasterized for
34,874.159 ms. Because external binding identity was no longer in the mask key,
this isolated the remaining mismatch to the nested stream itself: the scene and
resource generation stamps advanced from 2 to 3 even though the complete render
payload remained unchanged. The run was stopped at that decisive miss.

ProGPU `7056a710` compares the complete serialized header, resource records,
commands and arena while normalizing only scene and resource generation fields.
Every payload, descriptor, command, layout, scene id and other header change
still misses. The focused regression now rebuilds the equivalent nested stream
at a new generation, changes an unrelated binding, and requires a one-submission
hit. The focused test and all 19 native CTests pass locally.

## Focused native-input qualification

Before replacing the runtime with the retained-child optimization, the exact
`7056a710` Windows ARM64 PR artifact was paired with the rebuilt LibreWPF
Toolkit source overlay. The installed `progpu_native.dll` SHA-256 was
`F7E061E0F6B54A32AA57B34AD5C0E2E131573C023EB8951AEBABC9F15DC2E03A`,
and the rebuilt `ProGPU.Wpf.ToolkitApp.dll` SHA-256 was
`B8F134626AA684E726E37348EA3B7CFAB55B023A71A9AA17F3ADB6C788E9F232`.

The focused gate completed and exited zero. It first opened and dismissed the
Agenda overlay through AvalonDock's model activation contract, then resolved
the presented native input owner for the actual generated Agenda side-tab
`TextBlock`, delivered mouse move/down/up through the ProGPU host, observed the
Agenda overlay open, moved the real pointer away, and observed dismissal. It
repeated the same sequence for Contacts and wrote
`ProGPU WPF Toolkit live AvalonDock auto-hide validation succeeded.` to the
status file. This qualifies the source/native input route and the dispatcher
sequencing fix; it does not qualify the newer retained-child performance
revision.

ProGPU PR #173 merged branch head `81e5b486` as `910571ae`. It retains one
scratch picture child, preserves exact-snapshot scene identity, accounts only
submission deltas, includes child ownership in the native memory inventory, and
propagates device loss through the owned child chain. Local Apple M3 Pro/Metal
validation passes all 19 native CTests. A fresh exact Windows ARM64 artifact for
the merged revision is still required for the final performance and package
gates below.

## Acceptance criteria

The post-change run must provide all of the following evidence:

1. the native runtime binary hash matches the exact successful Windows ARM64
   artifact for the tested ProGPU commit;
2. the first occurrence of each eligible scene and descriptor is a cache miss;
3. unchanged nested scenes reused by later parent generations report
   `cacheHit=1` and do not emit another child raster render for that descriptor;
4. descriptor changes create their own raster rather than sampling an
   incompatible retained texture;
5. the Toolkit/AvalonDock live input sequence completes successfully;
6. native memory inventory accounts for the shared backing once and releases
   it after the owning engine/cache is retired;
7. the final LibreWPF exact-package Windows ARM64 gate passes with the merged
   ProGPU revision.

Until those conditions are recorded below, this report proves the regression
and defines the comparison but does not qualify the fix.

## Final replacement result

ProGPU `main` build
[35610787524](https://github.com/wieslawsoltes/ProGPU/actions/runs/35610787524)
completed successfully at merged commit
`910571ae18c5b8ec7f1fe41908fd8aa2304844e0`. The exact
`ProGPU.Backend.Native.0.1.0-preview.3101.ci.nupkg` Windows ARM64 payload had
these SHA-256 values:

- `progpu_native.dll`:
  `19E92448928F791116376E72DB5F3039890C6834044CD36AF68FF2244C747804`;
- `progpu_native_dawn.dll`:
  `6D0400F4846F3B771A1E43397C3ECCF511FC7C6369341E7C811A9D35CC526940`;
- `progpu_native_direct2d.dll`:
  `E78EC9A7F766DFEB5AFE202365D38E6CEA8CF64070047AA696F18C1B4B59C061`.

Those three binaries were installed together in the Windows 11 ARM64
Parallels guest. The source-overlay Toolkit application was rebuilt with
`NativeMilWgpu`, ARM64, and native MIL hit testing enabled; its application
assembly SHA-256 was
`778948DB04A1D10F3D12F9F869047B9EF7FCE22DB318656D30E29F3590D40723`.

The first 1934 by 1210 picture mask paid the expected cold compilation and
render cost (`renderMs=26617.522`). Stable descriptors in generation 3 then
reported `cacheHit=1` with zero create, binding-update, preparation, and render
time. Generations 4 through 6 repeated all nine initial descriptors with the
same zero-cost hits. Later layout changes created new descriptors as misses,
while their subsequent generations reused them. This distinguishes correct
descriptor invalidation from the earlier every-generation rerasterization.

The complete Toolkit/AvalonDock live gate exited zero. It exercised the native
host, transient-surface quiescence, editors, popups, child windows, message
boxes, virtualized data, automation, source-backed documents and anchorables,
auto-hide input, floating editor input, redocking/lifecycle, and serialization.
The final status was:

`ProGPU WPF Toolkit live input validation succeeded: logical 967x605, pixels 1934x1210, viewport 1934x1210@0,0, dpi 2.`

During qualification, the floating editor exposed that Extended.Wpf.Toolkit
5.1.2 embeds its own legacy `Microsoft.Windows.Shell.WindowChromeWorker`. That
worker treats `WindowInteropHelper.Handle` as a user32 HWND, but a portable
presentation source deliberately publishes an opaque identity. The Toolkit
adapter now clears the embedded chrome before a new floating Window is shown
and installs source-built WPF's typed portable `WindowChrome` instead. Its
backend guard reads `ConfiguredMediaBackend` without freezing the startup
choice. The focused project-graph test passes, and the exact Windows live gate
proves the floating host no longer crashes.

The merged ProGPU CI matrix passed the Direct2D/WebGPU cache regressions,
native memory inventory checks, Windows ARM64 native/package consumers, and
engine destruction paths. The inventory includes retained picture-mask
textures exactly once and cache teardown is owned by engine destruction. The
successful application run additionally passed its transient-surface
quiescence check and exited cleanly. Together with the exact merged-main
package provenance above, this satisfies this report's replacement acceptance
criteria. LibreWPF pull-request CI remains a separate exact-head merge gate.
