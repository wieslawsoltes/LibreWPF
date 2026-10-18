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
