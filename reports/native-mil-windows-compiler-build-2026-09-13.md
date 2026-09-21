# Native MIL Windows compiler dependency build

Acceptance application: `ProGPU.Wpf.ShowcaseApp`. User action: startup and first
source point/region input. Blocking path: the shared native WebGPU device's shader
compiler, independently of the installed-WARP query execution defect.

ProGPU `00972519` adds `eng/build-wgpu-native-windows.ps1`: an isolated Windows
dependency builder retaining the exact Silk native/header ABI, pinned wgpu
revision, Rust/Cargo 1.98.1 and preserved DXC-feature lock. It validates the
resolved HAL feature and output architecture before publishing a fresh artifact
with hashes and build provenance. No upstream renderer implementation is patched.
The original metadata overlay enables the existing optional HAL feature through
Cargo's dependency mechanism. No system DLL, NuGet cache, application output,
compiler default or WPF dependency pin changes.

Six input checks pass on both macOS and Windows. They cover Unicode/CRLF,
idempotent retries, preservation of unexpected changes, native UTF-8 capture,
child failure/encoding restoration and lock digest. Both normal Windows native
PR lanes now run these checks; actionlint passes. The real ARM64 VM build completes
in 3m35s and verifies the source/header/feature graph, unchanged lock and PE target.
New DLL SHA256: `a0cdbedccc490377b94c4e7cf8506d65c85bbc0be1c6cf4d70d2a04c791806e3`.
The complete independent native owner-query fixture passes with that DLL and the
same explicit-WARP/DXC diagnostic managed assembly and current native query DLL.
Loaded dependency, compiler and development-runtime paths were checked in child
process 3944; exit 0. Point, bounds and ellipse submissions took 1522.061, 1599.800
and 2030.964 ms. These are reproduction observations, not controlled benchmarks.
This does not qualify the current managed package, system WARP, x64 or Showcase.

Current system WARP was re-read: version `10.0.26100.9278`, SHA256
`750d6535099e15148103fbf75d1925d8ff590ff62124630aa7f687ddaab4b82a`.
It is unchanged from the failing system control. Windows reports build
`10.0.26200.9445`; OS version alone does not identify the graphics runtime.

Build `34784103259` at preceding ProGPU head `f6e60a1b` is confirmed in progress;
it is not a pass or a missing process. PR139 and WPF PR115 are open, draft and
GitHub-mergeable, but that is not qualification. Latest fetched ProGPU main is
already an ancestor. Existing physical dependency worktrees remain untouched.

Next: connect typed fail-closed compiler selection
and exact-ABI loading/package ownership for both renderers, then qualify current
Windows package/render/input and core source application paths. The independent
system-runtime failure, broader deferred scope, SDK gates and ordered merges
remain open. Development WARP is testing-only and must not be redistributed.
ActivityMonitor work is explicitly out of scope per the user's latest direction.
