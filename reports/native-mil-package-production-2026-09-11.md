# Native MIL package production and qualification checkpoint

Source qualification revision: LibreWPF `879396946`, LibreWinForms `fcbd4e5f6`,
ProGPU `b54db165`. The prepared source checkout is clean at those revisions.

Both Windows and both Linux native jobs in ProGPU run 34553573493 passed.
Their runtime artifacts were downloaded into isolated staging with run/head and
archive digest records. LibreWPF run 34554137461 passed Windows managed payload
production and canonical source integration; those artifacts are staged too.

While macOS CI remained queued, separate local `--build-only --rid osx-x64` and
`--build-only --rid osx-arm64` invocations compiled both providers, all required
SDK archives, and the configured test/sample targets. Neither used a reduced
profile. Both architectures then passed all 19 CTest tests: ARM64 in 11.41 seconds
and translated x64 execution on this ARM64 Mac in 24.41 seconds. These are local
test results, not native Intel hardware or full macOS CI qualification.

The assembled six-RID staging directory includes Windows Direct2D DLLs and the
required SDK files. The explicit WPF `--build-packages-only` lane exited zero,
creating the ProGPU dependency set and LibreWPF.Transport, LibreWPF.ProGPU and
LibreWPF.Sdk packages. Windows PresentationCore/DirectWriteForwarder/IJW assets
were consumed through the existing payload property. No package guard was
disabled. This establishes production, not release qualification.

The normal SDK gate subsequently started with `NativeMilWgpu`, mandatory native
host validation and the exact package snapshot. Protocol verification passed
(143 commands, 141 complete layouts). The external Avalonia package-consumer
build is currently running; the full SDK gate has not completed.

Separately, the unchanged DirectX comparison script found zero pixel differences
for HelloTriangle and HelloTexture between native Windows, ProGPU D3D12 and
ProGPU Linux Vulkan captures from the same run. Linux used llvmpipe. Metal and
broader DirectX parity remain open. Detailed artifact identities, hashes and
partial differential JSON are under `artifacts/native-exact-b54db165.dBPf7B`.

Remaining: finish the normal package/runtime gates, required platform/VM and
Metal evidence, final-head CI, and ordered merges. Broader API parity remains
explicitly outside this core-delivery checkpoint, not declared complete.
