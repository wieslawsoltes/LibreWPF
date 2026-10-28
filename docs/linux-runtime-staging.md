# Staging the source Xlib dependency

The optional fourth argument of `eng/progpu-stage-ci-native-runtimes.sh` stages
the Linux dependency carried by `ProGPU.Backend`. It uses the same exact commit,
successful whole Build, artifact and package version as the native/SDK and DX12
payloads. Existing two- and three-argument callers keep their behavior.

```sh
./eng/progpu-stage-ci-native-runtimes.sh "$qualified_progpu_commit" \
  "$progpu_root/artifacts/progpu-native/package" \
  "$progpu_root/artifacts/progpu-dx12/package" \
  "$progpu_root/artifacts/progpu-xlib/package"
```

The release extractor also accepts paired `--xlib-package` and
`--xlib-destination` arguments. It verifies the package ID, version, repository
and exact source commit before extracting any selected runtime. Xlib staging
requires both Linux x64/arm64 inventories, matching producer and dependency
receipts, the versioned extension, ABI-test completion, every original file hash,
and the corresponding ELF architecture. Extensionless license files retain their
original path and bytes. Malformed or incomplete candidates are discarded before
the Xlib destination is published. Existing destinations are never overwritten.

This supplies the future source-first package build with the real producer
payload; it does not patch a consumer DLL or a NuGet cache. The helper does not
advance a gitlink or qualify an unfinished ProGPU Build. Call sites and dependency
pins must be updated together after the producer is fully qualified.

Validation covers existing native/SDK/DX12 staging, exact whole-Build selection,
optional Xlib argument routing, changed provenance, partial inventories and
changed bytes. An actual local package control preserves all 14 corrected
Linux payload files and rejects the original malformed license path without
publishing a destination. That local package is a staging control, not a
whole-producer or application qualification result. The mixed-session source
application remains tracked in [#187](https://github.com/wieslawsoltes/LibreWPF/issues/187).

Ordinary source harnesses do not inherit ProGPU's repository targets. The
multi-window harness and source test project explicitly import
`eng/LibreWPF.XlibRuntime.targets` at their executable/test boundary. This selects
the actual source root's staged dependency and reuses its producer asset target;
it does not copy libraries into every referenced project. Portable output retains
both Linux RIDs, RID-specific output selects only its own assets, and other OS
targets remain independent. The original headless default/GL/Vulkan scenarios
stage the exact successful producer before creating their first window.

The additional real-payload control checks all original bytes and copy metadata
through both source imports, including repeated imports and library opt-outs.
Source runtime imports normalize their own path copy, so an explicit global
`ProGpuSourceRoot` works with or without a trailing separator. This keeps the
caller's property unchanged and covers both automatic staging discovery and
an explicit runtime root. The original no-separator case selected no assets;
the corrected control retains the complete payload inventory in either form.
It does not execute a Linux application. Package-mode SDK applications retain
normal NuGet runtime selection; no source-root inference replaces package identity.

The source integration selects ProGPU
`a37f235057ee58b26b3322864c62dfeb1d041715` after its entire
[Build 39093685811](https://github.com/wieslawsoltes/ProGPU/actions/runs/39093685811)
passed all 61 jobs and all 70 checks on that head passed. Both original Linux
runtime archives match their GitHub digests and pass the staging helper's exact
producer, dependency, ELF, license and byte validation. All 56 source-asset
evaluations pass with these files. This qualifies the selected producer and
asset selection; it does not qualify the final LibreWPF package application.

The subsequent source pin is the whole-qualified `905d52ac185278db60c312a709cf5aa045772dd5`
producer (all 61 Build jobs and 76 head checks), which also supplies native Close
enabled state. Its real Native/SDK, DX12 and Xlib payloads are staged together.
The host test executable additionally opts into `eng/LibreWPF.NativeRuntime.targets`:
ordinary project references package native libraries but do not copy them into a
source test output. The original full-suite run retained 43 missing-native-library
failures. The explicit test boundary now carries the original staged runtimes and
notices, excludes SDK archives, and retains portable versus RID-specific loader
layout. It does not alter library outputs or package-mode resolution.

All 32 real-payload selection controls pass for both source-root separators,
explicit runtime override, duplicate imports, executable/test admission, library
and disabled opt-outs, three actual RIDs and an unavailable RID. They compare
complete original file hashes and output/publish metadata. Native execution and
final application acceptance remain separate from these asset controls.

Canonical Forms integration also requires `external/LibreWinForms` to declare
that same exact ProGPU pin. The first `66391c64b` CI run correctly rejected the
older nested `a37f2350` pin before building. The coordinated Forms revision now
declares `905d52ac1`; WPF consumes that revision with its own identical producer.
The equality gate remains unchanged, and the original rejected run is retained.
Forms' complete 993-test Drawing source suite passes against this shared producer;
fresh whole CI remains required for both coordinated consumer heads.


## Windows producer staging preparation

The helper also accepts a fifth Windows staging directory and sixth exact
ProGPU source root. Existing two-, three- and four-argument callers retain their
behavior. CI and release preparation pass the complete contract:

```sh
./eng/progpu-stage-ci-native-runtimes.sh "$qualified_progpu_commit" \
  "$progpu_root/artifacts/progpu-native/package" \
  "$progpu_root/artifacts/progpu-dx12/package" \
  "$progpu_root/artifacts/progpu-xlib/package" \
  "$progpu_root/artifacts/progpu-wgpu-windows/package" \
  "$progpu_root"
```

The new Windows default payload belongs to `ProGPU.Backend`, separately from its
optional DXC package. Native/SDK packages deliberately exclude these shared
files. The extractor therefore selects both exact Windows prefixes from the same
Backend nupkg, validates its version/repository/commit, and calls that exact clean
producer checkout's `eng/progpu-wgpu-windows-runtime.py --verify` before publishing.
This preserves the original eight DLL/receipt/license files per architecture
without copying another repository's verifier or maintaining a second pin set.
Linux staging now selects only its two prefixes from that combined package.

Capability comes from the verifier tracked by the producer commit. An older
qualified source with no verifier and no Windows payload keeps its original
runtime policy. A deleted or locally changed new verifier, a mismatched source
commit, unexpected old-source payload, missing architecture or failed verification
rejects the candidate. No failed candidate is published and existing outputs are
never overwritten. The release CLI exposes this as `--windows-package`,
`--windows-destination` and `--producer-root`.

The existing source executable/test boundary also imports the producer asset
target for its staged Windows root. New-capability Windows executables fail
before build when their required DLL/receipt is missing. Ordinary libraries and
old qualified sources keep their behavior. If a self-contained raw native
artifact is supplied, its exact eight shared Windows files are excluded from
the separate native source-copy path; the Backend target owns their selection.
Private renderer resources, SDK exclusions and source-root ownership stay intact.

These are staging and source-asset controls. No ProGPU/Forms source pin changes,
new native runtime execution, original input changes or application qualification
are part of this preparation. The whole successful ProGPU producer remains a
prerequisite for actual new-payload validation and consumer pin advancement.

Validation of this preparation passes 23 package extraction/provenance controls,
11 canonical workflow/whole-producer selection controls, and 19 actual MSBuild
evaluations across seven Windows source-boundary controls. The current qualified
producer's unchanged files also pass all 56 Linux source-asset and 32 native
source-asset evaluations. The new-capability fixtures record imports and bytes;
they do not contain or execute a replacement native producer.
