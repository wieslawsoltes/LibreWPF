# Native picture-mask raster reuse on Windows ARM64

Date: 2026-09-20

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

## Replacement under test

[ProGPU PR #173](https://github.com/wieslawsoltes/ProGPU/pull/173) retains
eligible picture-mask raster textures in the existing bounded semantic-picture
cache. Reuse is keyed by the complete nested scene, raster descriptor, engine
flags, device, and format. External image bindings and seeded captures remain
ineligible. Sampling transforms, opacity, guidelines, and span uniforms remain
parent-scene state rather than part of the retained raster.

The current implementation branch head recorded for this comparison is
`8dc588f8523253b373abe5c17db4cb028de5aa08`. The exact-head Windows ARM64
renderer job must finish successfully and supply the runtime used for the
post-change run. The final LibreWPF package graph must then pin the merged
ProGPU commit rather than this pull-request branch.

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

Pending the exact successful `8dc588f8` Windows ARM64 artifact and Parallels
rerun.
