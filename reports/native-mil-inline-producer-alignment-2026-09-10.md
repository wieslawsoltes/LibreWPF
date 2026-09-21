# Native inline producer source alignment

The canonical source graph now selects ProGPU
26a6030f6a72bdb2256a8fba80d79770086e5544 and LibreWinForms
17f0aaa8c8a8cd53ff8408811fbea8dd6339d12d. LibreWinForms selects the same
ProGPU commit. Original dirty root submodule work is not overwritten.

The producer adds measured inline paragraph C ABI/generated span bindings,
non-ink object identity, fractional advances and per-line metrics/extents.
The source-built WPF consumer still rejects inline controls and anchored
content until snapshot interaction, source measurement and real visual ownership
are connected. This source alignment does not enable an unsupported route.

ProGPU Build 34510947244 at the preceding 38b6a7a4 completed with a failing native
NuGet C++ consumer. Dynamic consumers and native RID builds passed, but MIL's
exported static SDK omitted its required Direct2D core archive/dependency.
The new producer stages that archive for all desktop RIDs and publishes the
transitive CMake dependency. Its local installed SDK consumer links and runs.
Do not reuse the failed build's artifacts as final package qualification.

Producer component evidence: native CTest 20/20, generated contract verification,
zero-warning/error managed binding and development consumer builds, passing
default local consumer and static SDK consumer. These results do not replace
fresh exact-head all-RID packages, WPF native application, Windows VM or final
platform gates. The latest WPF source-table runtime checkpoint remains 8e13a7164.

Implementation detail and CI diagnosis are in ProGPU:
docs/native-mil-inline-paragraph-2026-09-10.md and
docs/native-mil-sdk-static-dependencies-2026-09-10.md. Merge order remains
ProGPU #139, LibreWinForms #29, LibreWPF #115, after their actual gates close.
