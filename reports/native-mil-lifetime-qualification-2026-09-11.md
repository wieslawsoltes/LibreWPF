# Native delivery lifetime checkpoint

The prepared source build of `ProGPU.Wpf.RealApplicationRunHarness` passed all
three existing `--portable-application-lifetime-only` scenarios on macOS arm64:

- `last-window`
- `main-window`
- `explicit`

Each reported success and exited zero. They ran serially using the existing
Release/net10.0 harness in `artifacts/native-core-build.KvxVug/wpf-rebase`, with
the prepared ProGPU native build/runtime directories on `DYLD_LIBRARY_PATH`.
This extends the local source-built application evidence; the prepared checkout
has mirrored source changes and is not an exact-head package qualification.

Inspection of the current SDK `ProGPU.Wpf.Sdk.PortableBootstrap.cs` confirms
explicit native selection initializes native media services and registers typed
window activation on Windows as well as other desktop systems. Only non-native
Windows selection takes the early return. Historical delivery checkpoints that
describe the earlier Windows activation guard are not current code evidence.
The architecture guard remains x64/ARM64, and Windows package execution still
requires actual qualification; bootstrap inspection does not prove it.

Exact-head CI runs observed active after dependency alignment:

- ProGPU: 34553573493, head b54db165.
- LibreWinForms: 34553597773, head fcbd4e5f6.
- LibreWPF: 34553616991, head c46a5476d.

No failures were reported at this observation. Queued/running jobs are not passes.
Remaining delivery work includes final CI, exact package production/consumption,
and required platform/native comparison gates. Broader parity remains open.

## Package-production prerequisite check

The prepared checkout's explicit `eng/progpu-wpf-sdk-ci.sh --build-packages-only`
lane was executed serially with the existing prepared native runtime staging
directory. It exited 1 at the native package runtime guard: the `win-x64`
`progpu_native.dll` has not been staged. No validation bypass was used. The
partial packages emitted before the error are unqualified and must not be
consumed as release artifacts. This run did not reach WPF transport packaging.

The exact-head ProGPU workflow provides `progpu-native-runtime-${rid}` artifacts
from the complete native jobs. Wait for those jobs and consume their matching
revision artifacts for final package production; do not relabel the older local
payloads as current-head evidence.
