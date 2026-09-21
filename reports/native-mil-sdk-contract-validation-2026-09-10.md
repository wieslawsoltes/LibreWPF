# Native MIL SDK contract validation

The acceptance path is the existing package-mode Showcase/Toolkit SDK gate.
Its final `ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface` source-contract test
failed before application qualification could be reported. This test must agree
with the integrated upstream package graph without removing provenance checks.

## Source-contract repairs

- ProGPU central package management remains enabled except for its independently
  versioned ACadSharp project. Assert both exact conditional declarations.
- The SDK workflow now has six qualified checkouts and nine qualified artifact
  references: fifteen total, not fourteen. Keep every checkout/artifact guard.
- The SDK script contains eleven no-build project invocations, including the
  added source Application.Run lifetime-scenario loop. Do not remove that loop.
- The package audit distinguishes `progpu_source_commit` from the explicit
  expected `progpu_package_commit`; assert both, retaining strict repository
  metadata verification rather than an ancestor-only source substitution.
- The shared test path locator accepts directory requests as well as files.
  File-specific callers still assert file existence explicitly.

The exact SDK graph test now passes under the repository's explicit VSTest path.
The initial `dotnet test` command stopped at the pinned SDK's Microsoft Testing
Platform selection and was not a test result. No product renderer, package input,
quality tolerance, feature admission or required gate changes in this repair.

## Current validation evidence

At LibreWPF `4d12967cb` with ProGPU `9e05651a` and LibreWinForms `c67b04a8c`:

- ProGPU local renderer tests: 4,577 pass, seven platform-specific skips.
- ProGPU headless tests: 280/280; native interop contracts: 121/121.
- Full Windows ARM64 native suite: 20/20 in 553.72 seconds. The graphics suite
  takes 542.88 seconds under concurrent host build load; this is not a controlled
  performance comparison with the earlier 314-second run.
- Windows direct masked-image before/after pixels are unchanged: maximum delta
  1, zero over-tolerance pixels, mean 0.03784963348765432. The after benchmark
  verifies its native library against build SHA-256
  `D8F3636B09FA24714013601BB8EAFDC790E3A8553C69876FEBDA1A0877650578`.
- The real source-built native MIL host passes retention, image DPI/clear/refill,
  text collapse, WriteableBitmap, Toolkit header collapse, geometry selection,
  native input and device recovery. Build: no errors, two warnings (an unused
  display-metrics event and a null-check style suggestion).
- Exact-head canonical WinForms source integration and Windows managed-payload
  CI pass. Their artifacts were downloaded into the isolated validation checkout;
  the old managed payload remains separate and is not substituted.

Prepared checkout logs: `artifacts/native-host-4d12967cb-9e05651a.log`,
`artifacts/wpf-graph-fix-build.log`, `artifacts/wpf-sdk-graph-fix-test.log`.
ProGPU local logs are under its `artifacts/release-hour/native-masked-image-*`.
Windows guest logs are `artifacts/masked-image-after-{build,test}.log` and
`artifacts/native-9e05651a-win-arm64-tests.log`.

## Still open

The first broad bridge-suite invocation lacked the explicit native-library search
path and also exposed retained-source fixture/replay and older source-assertion
failures. It was stopped and its log retained; it is not a complete valid pass.
Those failures still require triage with the correct runtime environment.
Full native Showcase/Toolkit package-mode validation, required platform gates
and exact-head CI remain outstanding. Default managed SDK CI and source-host
success alone do not qualify native package applications. PRs remain drafts.
