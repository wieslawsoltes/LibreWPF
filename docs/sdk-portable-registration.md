# Required portable startup registration

The SDK's default managed WPF initializer must successfully register the actual
source PresentationFramework activation service before clipboard setup or
application entry. Previously this branch discarded the Boolean result, unlike
Native MIL and explicit native-modal startup. A missing or mismatched source
assembly could therefore continue without a portable window host.

The default branch now rejects that configuration immediately with a specific
registration error. Ordinary Windows managed startup, Forms-only applications,
non-entry referenced executables, native opt-outs and headless/Web selection keep
their existing paths. Successful registration still uses the same host factory
and provider services.

The compiler fixture in `eng/tests/test_wpf_sdk_bootstrap.py` builds the actual
packaged initializer with deliberately controlled source-service doubles. A false
registration reproduces the old failure in managed and mixed entry applications:
both reached application entry and clipboard setup. The corrected initializer
must reject both. Native renderer/compiler variants keep their existing rejection;
Forms-only and ordinary Windows managed entry remain independent. Existing
referenced-executable controls remove desktop dependency DLLs before invoking the
reference, proving that the early return does not load those services.

This is bootstrap correctness, not qualification of the application reported in
PR #307. Current source `Window.CreateSourceWindow` uses portable activation before
the Windows `SetupInitialState` path. Its portable startup placement uses the
host's monitor service, and its icon state uses the owned executable-icon reader.
The canonical Drawing implementation does not use Windows locale/GDI calls for
non-Windows system fonts. Fixed shim monitor dimensions, fake default positions,
byte-widened executable paths and hard-coded LCIDs do not establish those source
contracts. Final matching package and reported-application validation remain
required; this guard does not claim to repair an unavailable source registration.

The current public OpenDevelop tree was inspected at
[`c25feb04ae267e225cace63dcdd4c7a26fdb68a8`](https://github.com/lextudio/OpenDevelop/tree/c25feb04ae267e225cace63dcdd4c7a26fdb68a8).
Its [entry project](https://github.com/lextudio/OpenDevelop/blob/c25feb04ae267e225cace63dcdd4c7a26fdb68a8/src/Main/SharpDevelop/SharpDevelop.csproj)
explicitly enables the portable bootstrap on non-Windows hosts and selects both
WPF and Forms. Its [SDK selection](https://github.com/lextudio/OpenDevelop/blob/c25feb04ae267e225cace63dcdd4c7a26fdb68a8/global.json)
is preview.69. That current configuration is useful for final package validation;
it does not establish which exact package bytes or startup registration failed in
the earlier report. No changes were made to OpenDevelop or the separate local
SharpDevelop checkout during this inspection.
