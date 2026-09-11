# Package coherence base integration

Acceptance path: the package-mode Showcase and mixed WPF/WinForms applications
must load the coordinated LibreWPF/ProGPU/LibreWinForms package closure. This
batch integrates LibreWPF base 4c4c2a8a2 (PR #124), resolving seven conflicted files.
It does not admit native startup or claim application qualification.

## Resolution

- Preserve the base's canonical WinForms generated package paths, dependency
  manifest entries and final output-copy target, including missing-asset errors.
- Preserve native startup text/geometry assertions, exact-head payload checks,
  explicit package-build-only mode and separate build/run invocations.
- Use the base's effective LibreWPF and ProGPU version resolution in all three
  smoke consumers. Preserve trimming of configured values and remove superseded
  duplicate ProGPU fields/helpers.
- Emit both SDK ProGPU version properties from one resolved value. Explicit
  ProGpuPackageVersion takes priority, then ProGpuRuntimePackageVersion; the
  runtime default now matches preview.62. Audit both emitted properties.
- Retain the base's portable Dispatcher null-window shutdown fix and coordinated
  LibreWinForms override. Keep existing source/package compatibility assertions.

## Focused evidence

The merged bridge test project builds with zero errors and 20 analyzer warnings.
All 94 managed project-graph tests pass using the repository's explicit VSTest
path. A direct dotnet test attempt stopped at the existing global.json
Microsoft.Testing.Platform/VSTest mismatch before test execution.
Shell syntax, documentation/package-table verification and git diff whitespace
checks pass. Both changed SDK harness projects build with zero warnings/errors
after restoring their previously absent assets; the initial no-restore attempt
failed with NETSDK1004.

Direct execution of the real SDK generation target confirms both emitted
ProGPU properties agree for all three cases: conflicting explicit/runtime
inputs select the explicit version; a runtime-only override selects that
version; no override selects preview.62. Generated files are under the prepared
worktree's artifacts/merge-version-{explicit,runtime,default} directories.
No packages or applications were launched by these generation checks.

The dependency gitlinks remain ProGPU 26a6030f and LibreWinForms 17f0aaa8.
Original dirty root submodule directories are not modified. This integration
does not make those pins the final producer head or requalify old payloads.
Remaining work includes inline/anchored source integration, ProGPU SVG CI,
final dependency alignment, package/application/platform qualification and the
ordered ProGPU #139, LibreWinForms #29, LibreWPF #115 merges.
